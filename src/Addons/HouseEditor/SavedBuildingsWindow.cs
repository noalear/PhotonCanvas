using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.App;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>Combo boxes and text boxes for a saved building's details: its name, type, theme, district, tags and notes.</summary>
sealed class PrefabFields
{
    public readonly TextBox Name = new() { Watermark = "What it's called: The Prancing Pony, Corner shop…" };
    public readonly ComboBox Type = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public readonly ComboBox Theme = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = BuildingThemes.Names.ToList() };
    public readonly ComboBox District = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public readonly TextBox Tags = new() { Watermark = "Comma between each: corner, ruined, two doors, river" };
    public readonly TextBox Notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, Watermark = "Anything to remember about it" };
    List<string> _types = [.. HousePrefabs.Types], _districts = [.. HousePrefabs.Districts];

    public Control Panel() => new StackPanel
    {
        Spacing = 8,
        Children =
        {
            Field("Name", Name),
            Field("Type", Type, tip: "What kind of building it is. The city builder picks buildings by type to fill a map."),
            Field("Theme", Theme, tip: "The look it's for: it's set from the building's own theme when it's first saved."),
            Field("Goes in", District, tip: "Where in a town it belongs, for laying out a map: the market, the docks, the noble quarter…"),
            Field("Tags", Tags, tip: "Words to find it by, a comma between each."),
            Field("Notes", Notes),
        },
    };

    public void Show(HousePrefab p)
    {
        Name.Text = p.Name;
        // A type or district typed in somewhere else (another add-on, a shared file) is kept in the list.
        _types = [.. HousePrefabs.Types];
        if (!_types.Contains(p.Type, StringComparer.OrdinalIgnoreCase)) _types.Insert(_types.Count - 1, p.Type);
        Type.ItemsSource = _types.ToList();
        Type.SelectedIndex = Math.Max(0, _types.FindIndex(t => string.Equals(t, p.Type, StringComparison.OrdinalIgnoreCase)));
        Theme.SelectedIndex = Math.Max(0, BuildingThemes.Names.ToList().FindIndex(t => string.Equals(t, p.Theme, StringComparison.OrdinalIgnoreCase)));
        _districts = [.. HousePrefabs.Districts];
        if (!_districts.Contains(p.District, StringComparer.OrdinalIgnoreCase)) _districts.Add(p.District);
        District.ItemsSource = _districts.ToList();
        District.SelectedIndex = Math.Max(0, _districts.FindIndex(t => string.Equals(t, p.District, StringComparison.OrdinalIgnoreCase)));
        Tags.Text = string.Join(", ", p.Tags);
        Notes.Text = p.Notes;
    }

    public HousePrefab Read(HousePrefab p) => p with
    {
        Name = string.IsNullOrWhiteSpace(Name.Text) ? "Building" : Name.Text.Trim(),
        Type = Type.SelectedIndex >= 0 && Type.SelectedIndex < _types.Count ? _types[Type.SelectedIndex] : "Other",
        Theme = Theme.SelectedIndex >= 0 ? BuildingThemes.Names[Theme.SelectedIndex] : p.Theme,
        District = District.SelectedIndex >= 0 && District.SelectedIndex < _districts.Count ? _districts[District.SelectedIndex] : "Anywhere",
        Tags = [.. (Tags.Text ?? "").Split(',', ';').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
        Notes = Notes.Text?.Trim() ?? "",
    };
}

/// <summary>
/// Saving the building being drawn to the saved buildings: its name, type, theme, where it goes in a town, tags and
/// notes. When it was opened from one, it's saved over that one unless "Save as a new building" is ticked.
/// Returns the details to save (the design in it), or null when cancelled.
/// </summary>
public sealed class SaveBuildingWindow : Window
{
    readonly PrefabFields _fields = new();
    readonly CheckBox _asNew = new() { Content = "Save as a new building (keep the old one as it was)" };
    readonly HousePrefab _start;

    public SaveBuildingWindow(HousePrefab start, bool existing)
    {
        _start = start;
        Title = existing ? "Save the building" : "Save the building to reuse";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _fields.Show(start);
        _asNew.IsVisible = existing;
        var save = new Button { Content = "Save", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        save.Click += (_, _) =>
        {
            var p = _fields.Read(_start);
            if (_asNew.IsChecked == true) p = p with { Id = Guid.NewGuid(), Created = default };
            Close(p);
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                Hint("It goes in Saved buildings, to open again and change, to drop into another building as a ready-made part, or for the city builder to lay out on a map."),
                _fields.Panel(),
                _asNew,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel } },
            },
        };
        Opened += (_, _) => { _fields.Name.Focus(); _fields.Name.SelectAll(); };
    }
}

/// <summary>What to do with a saved building picked in <see cref="SavedBuildingsWindow"/>.</summary>
public enum PrefabUse { Open, Place }

/// <summary>
/// The saved buildings: found by name, type, theme, where they go and tags, each with its picture and size. Pick one
/// to open it and change it, or to put it into the building being drawn; change its details, copy it, take it away,
/// or share it as a file. Returns what to do and with which, or null when closed.
/// </summary>
public sealed class SavedBuildingsWindow : Window
{
    readonly string _folder;
    readonly TextBox _search = new() { Watermark = "Find by name, tag or note" };
    readonly ComboBox _type = new() { MinWidth = 140 }, _theme = new() { MinWidth = 120 }, _district = new() { MinWidth = 130 };
    readonly ListBox _list = new() { SelectionMode = SelectionMode.Single };
    readonly PrefabFields _fields = new();
    readonly Image _picture = new() { Height = 200, Stretch = Stretch.Uniform };
    readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _open = new() { Content = "Open to change", Classes = { "primary" }, MinWidth = 130 };
    readonly Button _place = new() { Content = "Put into this building", MinWidth = 150 };
    readonly Button _keep = UiKit.Button("Save details"), _copy = UiKit.Button("Make a copy"), _delete = UiKit.Button("Delete"),
        _export = UiKit.Button("Save as a file…", tip: "A copy of it as a file, to keep or share"), _import = UiKit.Button("Add from a file…", tip: "A building saved as a file, here or on another computer (or a building editor last.json)");
    readonly StackPanel _details = new() { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
    List<HousePrefab> _all = [], _shown = [];
    bool _loading, _deleteArmed;

    public SavedBuildingsWindow(string folder, Guid? current = null)
    {
        _folder = folder;
        Title = "Saved buildings";
        Width = 980;
        Height = 680;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        _search.Width = 220;
        foreach (var c in new Control[] { _search, _type, _theme, _district }) { c.Margin = new Thickness(0, 0, 8, 6); filters.Children.Add(c); }
        _search.TextChanged += (_, _) => Filter();
        foreach (var c in new[] { _type, _theme, _district }) c.SelectionChanged += (_, _) => { if (!_loading) Filter(); };
        _list.SelectionChanged += (_, _) => ShowPicked();

        var listSide = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(16, 14, 8, 0) };
        listSide.Children.Add(filters);
        listSide.Children.Add(new Border { CornerRadius = new(8), BorderBrush = Brush("Line"), BorderThickness = new(1), ClipToBounds = true, Child = _list }.With(b => Grid.SetRow(b, 1)));

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { _open, _place }) { b.Margin = new Thickness(0, 0, 8, 6); actions.Children.Add(b); }
        ToolTip.SetTip(_open, "Opens it in the building editor to change it (Save then saves over it)");
        ToolTip.SetTip(_place, "Puts it into the building you're drawing, on the floor you're on: click on the plan where it goes, [ and ] to turn it");
        var more = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { _keep, _copy, _delete }) { b.Margin = new Thickness(0, 0, 8, 6); more.Children.Add(b); }
        _details.Children.Add(new Border { CornerRadius = new(8), Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF3, 0xEC)), Child = _picture });
        _details.Children.Add(_about);
        _details.Children.Add(actions);
        _details.Children.Add(_fields.Panel());
        _details.Children.Add(more);
        var side = new ScrollViewer { Width = 380, Margin = new Thickness(8, 14, 16, 0), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _details };

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), Margin = new Thickness(16, 10, 16, 14), ColumnSpacing = 8 };
        bottom.Children.Add(_status);
        bottom.Children.Add(_import.With(c => Grid.SetColumn(c, 1)));
        bottom.Children.Add(_export.With(c => Grid.SetColumn(c, 2)));
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90 };
        close.Click += (_, _) => Close(null);
        bottom.Children.Add(close.With(c => Grid.SetColumn(c, 3)));

        var body = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto") };
        body.Children.Add(listSide);
        body.Children.Add(side.With(c => Grid.SetColumn(c, 1)));
        body.Children.Add(bottom.With(c => { Grid.SetRow(c, 1); Grid.SetColumnSpan(c, 2); }));
        Content = body;

        _open.Click += (_, _) => { if (Picked is { } p) Close((PrefabUse.Open, p)); };
        _place.Click += (_, _) => { if (Picked is { } p) Close((PrefabUse.Place, p)); };
        _keep.Click += (_, _) => KeepDetails();
        _copy.Click += (_, _) => Copy();
        _delete.Click += (_, _) => Delete();
        _export.Click += async (_, _) => await Export();
        _import.Click += async (_, _) => await Import();
        Reload(current);
    }

    HousePrefab? Picked => _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? _shown[_list.SelectedIndex] : null;

    void Reload(Guid? pick = null)
    {
        _all = HousePrefabs.Load(_folder);
        _loading = true;
        static List<string> Of(string any, IEnumerable<string> values) => [any, .. values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];
        string? Was(ComboBox c) => c.SelectedIndex > 0 ? c.SelectedItem as string : null;
        void Set(ComboBox c, List<string> items, string? was)
        {
            c.ItemsSource = items;
            c.SelectedIndex = was is null ? 0 : Math.Max(0, items.FindIndex(i => string.Equals(i, was, StringComparison.OrdinalIgnoreCase)));
        }
        Set(_type, Of("Any type", _all.Select(p => p.Type)), Was(_type));
        Set(_theme, Of("Any theme", _all.Select(p => p.Theme)), Was(_theme));
        Set(_district, Of("Anywhere in town", _all.Select(p => p.District)), Was(_district));
        _loading = false;
        Filter(pick ?? Picked?.Id);
    }

    void Filter(Guid? pick = null)
    {
        pick ??= Picked?.Id;
        string? Chosen(ComboBox c) => c.SelectedIndex > 0 ? c.SelectedItem as string : null;
        _shown = [.. HousePrefabs.Matching(_all, Chosen(_type), Chosen(_theme), Chosen(_district), _search.Text)];
        _list.ItemsSource = _shown.Select(Row).ToList();
        int i = pick is { } id ? _shown.FindIndex(p => p.Id == id) : -1;
        _list.SelectedIndex = i >= 0 ? i : _shown.Count > 0 ? 0 : -1;
        _status.Text = _all.Count == 0
            ? "No saved buildings yet. Draw one in the building editor and press Save…, or add one from a file."
            : $"{_shown.Count} of {_all.Count} saved building{(_all.Count == 1 ? "" : "s")}.";
        ShowPicked();
    }

    Control Row(HousePrefab p)
    {
        var pic = new Image { Width = 88, Height = 66, Stretch = Stretch.Uniform, Source = Picture(p) };
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"{p.Type} · {p.Theme} · {p.District}", Classes = { "muted" } },
                new TextBlock { Text = Size(p) + (p.Tags.Count > 0 ? " · " + string.Join(", ", p.Tags) : ""), Classes = { "muted" }, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(2), Children = { pic, text } };
    }

    static string Size(HousePrefab p)
    {
        var f = p.Footprint ?? HousePrefabs.FootprintOf(p.Design);
        return $"{f.Width} × {f.Depth} squares ({f.WidthMm:0} × {f.DepthMm:0} mm), {f.Floors} floor{(f.Floors == 1 ? "" : "s")}";
    }

    Bitmap? Picture(HousePrefab p)
    {
        if (HousePrefabs.PicturePath(_folder, p.Id) is not { } path) return null;
        try { using var s = File.OpenRead(path); return new Bitmap(s); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
    }

    void ShowPicked()
    {
        _deleteArmed = false;
        _delete.Content = "Delete";
        var p = Picked;
        _details.IsEnabled = p is not null;
        if (p is null) { _picture.Source = null; _about.Text = ""; return; }
        _picture.Source = Picture(p);
        var f = p.Footprint ?? HousePrefabs.FootprintOf(p.Design);
        var doors = f.Doors.Select(d => d.Facing switch { PlanSide.Front => "front", PlanSide.Back => "back", PlanSide.Left => "left", _ => "right" }).Distinct().ToList();
        _about.Text = $"{Size(p)}, about {f.HeightMm:0} mm high" + (f.Roofed ? ", roofed" : "")
            + (doors.Count > 0 ? $". Doors out at the {UiKit.ListWords(doors)}." : ". No door out.")
            + $" Saved {p.Modified.ToLocalTime():d MMM yyyy, HH:mm}.";
        _fields.Show(p);
    }

    void KeepDetails()
    {
        if (Picked is not { } p) return;
        try
        {
            var saved = HousePrefabs.Save(_folder, _fields.Read(p));
            _status.Text = $"Saved {saved.Name}.";
            Reload(saved.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save it: {e.Message}"; }
    }

    void Copy()
    {
        if (Picked is not { } p) return;
        try
        {
            var copy = HousePrefabs.Save(_folder, HousePrefabs.Copy(_fields.Read(p)), PictureBytes(p));
            _status.Text = $"Made {copy.Name}.";
            Reload(copy.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't copy it: {e.Message}"; }
    }

    byte[]? PictureBytes(HousePrefab p)
    {
        try { return HousePrefabs.PicturePath(_folder, p.Id) is { } path ? File.ReadAllBytes(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void Delete()
    {
        if (Picked is not { } p) return;
        // Pressed twice to delete: there's no getting it back.
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            _delete.Content = "Press again to delete";
            _status.Text = $"Delete {p.Name} for good? Press Delete again (it can't be undone).";
            return;
        }
        try
        {
            HousePrefabs.Delete(_folder, p.Id);
            _status.Text = $"Deleted {p.Name}.";
            Reload();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't delete it: {e.Message}"; }
    }

    async Task Export()
    {
        if (Picked is not { } p) return;
        var name = string.Concat(p.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the building as a file",
            DefaultExtension = "json",
            SuggestedFileName = name + ".building.json",
            FileTypeChoices = [new FilePickerFileType("Saved building") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, HousePrefabs.ToJson(p));
            _status.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save it: {e.Message}"; }
    }

    async Task Import()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a saved building from a file",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Saved buildings") { Patterns = ["*.json"] }, new FilePickerFileType("All files") { Patterns = ["*"] }],
        });
        int added = 0;
        Guid? last = null;
        var bad = new List<string>();
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            if (HousePrefabs.Read(path) is not { } p) { bad.Add(Path.GetFileName(path)); continue; }
            // One already here with the same id comes in as a copy, so neither is lost.
            if (_all.Any(x => x.Id == p.Id)) p = HousePrefabs.Copy(p, p.Name);
            if (p.Name == "Building" && Path.GetFileName(path) is var fn && fn.Replace(".building.json", "").Replace(".json", "") is { Length: > 0 } n && n != "last") p = p with { Name = n };
            try { last = HousePrefabs.Save(_folder, p).Id; added++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { bad.Add(Path.GetFileName(path)); }
        }
        if (files.Count == 0) return;
        Reload(last);
        _status.Text = (added > 0 ? $"Added {added} building{(added == 1 ? "" : "s")} (no pictures yet: open one and save it to make its picture)." : "")
            + (bad.Count > 0 ? $" Couldn't read {UiKit.ListWords(bad)}." : "");
    }
}
