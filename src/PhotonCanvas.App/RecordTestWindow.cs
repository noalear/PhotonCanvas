using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.App;

/// <summary>
/// After burning a material test: photograph it, click the squares that came out well, and
/// save each as a tested recipe (with a photo of that square) for the material.
/// </summary>
public sealed class RecordTestWindow : Window
{
    sealed record Cell(Layer Layer, Rect2 Bounds, double Power, double Speed);

    readonly MaterialLibrary _lib;
    readonly Func<Task> _updatePhoto;
    readonly Func<CameraPhoto?> _photo;
    readonly double? _focus;
    readonly List<Cell> _cells;
    readonly HashSet<Cell> _picked = [];
    readonly Grid _grid = new();
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Basswood" };
    readonly ComboBox _category = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Material.Categories, SelectedIndex = 0 };
    readonly NumericUpDown _thickness = Number(0, 100, 0.5m, "0.##");
    readonly Control _newRows;
    readonly Stars _rating = new() { Value = 4 };
    readonly TextBox _notes = new() { Watermark = "Notes for the chosen squares (optional)" };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _save = new() { Content = "Save chosen squares", Classes = { "primary" }, IsEnabled = false, MinWidth = 170 };

    public RecordTestWindow(DocState state, MaterialLibrary lib, CameraPhoto? photo, Func<Task> updatePhoto, Func<CameraPhoto?> currentPhoto, double? focusMm)
    {
        _lib = lib;
        _updatePhoto = updatePhoto;
        _photo = currentPhoto;
        _focus = focusMm;
        Title = "Record material test results";
        Width = 900;
        Height = 760;
        MinWidth = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        // A material test is the "Test …" layers made by Laser → Test grid: one square each.
        _cells = state.Layers
            .Where(l => l.Name.StartsWith("Test ", StringComparison.Ordinal) && l.Name != "Test labels")
            .Select(l => (Layer: l, Shapes: state.ShapesOn(l.Id).ToList()))
            .Where(t => t.Shapes.Count > 0)
            .Select(t => new Cell(t.Layer, t.Shapes.Select(s => s.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b)), t.Layer.Laser.Power, t.Layer.Laser.Speed))
            .ToList();

        _material.ItemsSource = lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
        _newRows = new StackPanel { Spacing = 8, Children = { Field("Name", _newName), Field("Category", _category), Field("Thickness mm", _thickness) } };
        _material.SelectionChanged += (_, _) => _newRows.IsVisible = _material.SelectedIndex == 0;

        var update = Button("Update photo", "IcCamera", "Take a fresh camera photo of the burned test.");
        update.Click += async (_, _) =>
        {
            update.IsEnabled = false;
            _status.Text = "Taking a photo…";
            try { await _updatePhoto(); }
            finally { update.IsEnabled = true; }
            BuildGrid();
            _status.Text = _photo() is null ? "No camera picture yet: connect, calibrate the camera and turn on Camera on work area." : "Click the squares that came out well.";
        };
        _save.Click += (_, _) => Save();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();

        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Burn the test, click Update photo, then click every square you'd use again. Each becomes a tested recipe with a photo of that square.",
                },
                Field("Material", _material),
                _newRows,
                Field("How good", _rating),
                _notes,
                update,
                _status,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close, _save } };
        DockPanel.SetDock(form, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var root = new DockPanel();
        root.Children.Add(form);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _grid, Margin = new(0, 10, 0, 0), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        Content = new Border { Padding = new Thickness(16), Child = root };

        if (_cells.Count == 0)
        {
            _status.Text = "There's no material test in this design. Make one with Laser → Test grid, burn it, then come back here.";
            update.IsEnabled = false;
        }
        else _status.Text = photo is null ? "Burn the test, then click Update photo." : "Click the squares that came out well (Update photo first if this picture is from before the burn).";
        BuildGrid();
    }

    void BuildGrid()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        if (_cells.Count == 0) return;
        var powers = _cells.Select(c => c.Power).Distinct().Order().ToList();
        var speeds = _cells.Select(c => c.Speed).Distinct().Order().ToList();
        _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        foreach (var _ in powers) _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in speeds) _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        void At(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); _grid.Children.Add(c); }
        At(new TextBlock { Text = "mm/s ↓  power % →", Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 4) }, 0, 0);
        for (int i = 0; i < powers.Count; i++) At(new TextBlock { Text = $"{powers[i]:0.#}%", HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold }, 0, i + 1);
        for (int j = 0; j < speeds.Count; j++) At(new TextBlock { Text = $"{speeds[j]:0.#}", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 8, 0) }, j + 1, 0);
        var photo = _photo();
        foreach (var cell in _cells)
            At(CellButton(cell, photo), speeds.IndexOf(cell.Speed) + 1, powers.IndexOf(cell.Power) + 1);
    }

    Control CellButton(Cell cell, CameraPhoto? photo)
    {
        Control face = new TextBlock { Text = $"{cell.Power:0.#}%\n{cell.Speed:0.#}", TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 };
        if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, cell.Bounds.Inflate(1.5), 160) is { } jpg)
        {
            try { using var ms = new MemoryStream(jpg); face = new Image { Source = new Bitmap(ms), Stretch = Stretch.UniformToFill }; }
            catch (ArgumentException) { }
        }
        var b = new Button
        {
            Width = 84, Height = 84, Padding = new(2), Margin = new(3),
            Content = face,
            BorderThickness = new(3),
            BorderBrush = _picked.Contains(cell) ? Brush("Accent") : Brushes.Transparent,
        };
        ToolTip.SetTip(b, $"{cell.Layer.Name}: {ProcessSettings.From(cell.Layer).Summary()}");
        b.Click += (_, _) =>
        {
            if (!_picked.Remove(cell)) _picked.Add(cell);
            b.BorderBrush = _picked.Contains(cell) ? Brush("Accent") : Brushes.Transparent;
            _save.IsEnabled = _picked.Count > 0;
            _save.Content = _picked.Count > 1 ? $"Save {_picked.Count} squares" : "Save chosen square";
        };
        return b;
    }

    void Save()
    {
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; _newName.Focus(); return; }
            double t = (double)(_thickness.Value ?? 0);
            m = new Material { Name = name, Category = _category.SelectedItem as string ?? "Other", ThicknessMm = t > 0 ? t : null, HighlyReflective = Material.LooksReflective(name) };
        }
        else m = _lib.Materials[_material.SelectedIndex - 1];
        var photo = _photo();
        var recipes = new List<Recipe>();
        foreach (var cell in _picked.OrderBy(c => c.Speed).ThenBy(c => c.Power))
        {
            var r = new Recipe
            {
                Purpose = cell.Layer.Mode switch { LayerMode.Fill => RecipePurpose.Engrave, LayerMode.Score => RecipePurpose.Score, LayerMode.Image => RecipePurpose.Picture, _ => RecipePurpose.Cut },
                Title = $"{cell.Power:0.#}% · {cell.Speed:0.#} mm/s (material test)",
                Settings = ProcessSettings.From(cell.Layer),
                Rating = _rating.Value,
                Tested = true,
                TestedOn = DateTime.Now,
                FocusMm = _focus,
                Notes = _notes.Text?.Trim() ?? "",
            };
            if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, cell.Bounds.Inflate(1.5)) is { } jpg)
                r = r with { Photo = _lib.SavePhoto(r.Id, jpg) };
            recipes.Add(r);
        }
        _lib.Upsert(m with { Recipes = m.Recipes.AddRange(recipes) });
        _lib.Save();
        _status.Text = $"Saved {recipes.Count} recipe(s) to {m.Label}.";
        _picked.Clear();
        _material.ItemsSource = _lib.Materials.Select(x => x.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = _lib.Materials.FindIndex(x => x.Id == m.Id) + 1;
        BuildGrid();
        _save.IsEnabled = false;
    }
}
