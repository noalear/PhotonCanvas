using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Save settings as a recipe in the material library: which material, what for, how well it worked.</summary>
public sealed class SaveRecipeWindow : Window
{
    readonly MaterialLibrary _lib;
    readonly ProcessSettings _settings;
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Basswood, Black anodised aluminium" };
    readonly ComboBox _category = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Material.Categories };
    readonly NumericUpDown _thickness = Number(0, 100, 0.5m, "0.##");
    readonly Control _newRows;
    readonly ComboBox _purpose = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Enum.GetNames<RecipePurpose>() };
    readonly TextBox _title = new() { Watermark = "e.g. Clean cut, light char" };
    readonly Stars _rating = new();
    readonly CheckBox _tested = new() { Content = "I burned this and checked the result", IsChecked = true };
    readonly NumericUpDown _focus = Number(0, 60, 0.1m, "0.0");
    readonly TextBox _notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Watermark = "What you noticed: char on the back, needs masking tape, best at the edge…" };
    readonly CheckBox _usePhoto = new() { Content = "Attach a camera photo of the result" };
    readonly Image _photoView = new() { MaxHeight = 140, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    readonly byte[]? _photo;

    /// <summary>What was saved (null if cancelled).</summary>
    public (Material Material, Recipe Recipe)? Result { get; private set; }

    public SaveRecipeWindow(MaterialLibrary lib, Layer layer, double? focusMm, byte[]? photoJpeg)
    {
        _lib = lib;
        _settings = ProcessSettings.From(layer);
        _photo = photoJpeg;
        Title = "Save to material library";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var items = lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.ItemsSource = items;
        int current = layer.Material is { } r ? lib.Materials.FindIndex(m => m.Id == r.MaterialId) : -1;
        _material.SelectedIndex = current >= 0 ? current + 1 : 0;
        _category.SelectedIndex = 0;
        _newRows = new StackPanel
        {
            Spacing = 8,
            Children = { Field("Name", _newName), Field("Category", _category), Field("Thickness mm", _thickness, tip: "Leave at 0 for coatings, surfaces and anything where thickness doesn't matter.") },
        };
        _material.SelectionChanged += (_, _) => _newRows.IsVisible = _material.SelectedIndex == 0;
        _newRows.IsVisible = _material.SelectedIndex == 0;

        _purpose.SelectedIndex = (int)(layer.Mode switch
        {
            LayerMode.Fill => RecipePurpose.Engrave,
            LayerMode.Score => RecipePurpose.Score,
            LayerMode.Image => RecipePurpose.Picture,
            _ => RecipePurpose.Cut,
        });
        _focus.Value = focusMm is { } f ? (decimal)Math.Round(f, 1) : null;
        _rating.Value = 4;
        if (photoJpeg is not null)
        {
            _usePhoto.IsChecked = true;
            try { using var ms = new MemoryStream(photoJpeg); _photoView.Source = new Bitmap(ms); }
            catch (ArgumentException) { }
        }
        _usePhoto.IsVisible = _photoView.IsVisible = photoJpeg is not null;
        _usePhoto.IsCheckedChanged += (_, _) => _photoView.Opacity = _usePhoto.IsChecked == true ? 1 : 0.3;

        var save = new Button { Content = "Save", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();
        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = _settings.Summary(), FontWeight = FontWeight.SemiBold },
                    Field("Material", _material),
                    _newRows,
                    Field("Used for", _purpose),
                    Field("Short title", _title),
                    Field("How good", _rating),
                    _tested,
                    Field("Focus mm", _focus, tip: "Laser-to-material distance used (from the machine). Empty if it doesn't matter."),
                    _notes,
                    _usePhoto,
                    _photoView,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } },
                },
            },
        };
    }

    void Save()
    {
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _newName.Focus(); return; }
            double t = (double)(_thickness.Value ?? 0);
            m = new Material { Name = name, Category = _category.SelectedItem as string ?? "Other", ThicknessMm = t > 0 ? t : null };
        }
        else m = _lib.Materials[_material.SelectedIndex - 1];
        var recipe = new Recipe
        {
            Purpose = (RecipePurpose)Math.Max(0, _purpose.SelectedIndex),
            Title = string.IsNullOrWhiteSpace(_title.Text) ? _settings.Summary() : _title.Text.Trim(),
            Settings = _settings,
            Rating = _rating.Value,
            Tested = _tested.IsChecked == true,
            TestedOn = _tested.IsChecked == true ? DateTime.Now : null,
            FocusMm = _focus.Value is { } f ? (double)f : null,
            Notes = _notes.Text?.Trim() ?? "",
        };
        if (_photo is not null && _usePhoto.IsChecked == true) recipe = recipe with { Photo = _lib.SavePhoto(recipe.Id, _photo) };
        m = m with { Recipes = m.Recipes.Add(recipe) };
        _lib.Upsert(m);
        _lib.Save();
        Result = (m, recipe);
        Close();
    }
}

/// <summary>A 0–5 star rating you click.</summary>
public sealed class Stars : StackPanel
{
    int _value;
    readonly Button[] _stars = new Button[5];
    public event Action? Changed;

    public Stars()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        for (int i = 0; i < 5; i++)
        {
            int n = i + 1;
            var b = new Button { Classes = { "icon" }, Padding = new(2), MinWidth = 26, FontSize = 17 };
            b.Click += (_, _) => Value = Value == n ? n - 1 : n; // click the top star again to lower it
            ToolTip.SetTip(b, n switch { 1 => "Poor", 2 => "Usable", 3 => "Good", 4 => "Very good", _ => "Perfect" });
            _stars[i] = b;
            Children.Add(b);
        }
        Show();
    }

    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 5); Show(); Changed?.Invoke(); }
    }

    void Show()
    {
        for (int i = 0; i < 5; i++)
        {
            _stars[i].Content = i < _value ? "★" : "☆";
            _stars[i].Foreground = i < _value ? Brush("Warn") : Brush("TextMuted");
        }
    }

    public static string Text(int rating) => rating <= 0 ? "" : new string('★', rating) + new string('☆', 5 - rating);
}
