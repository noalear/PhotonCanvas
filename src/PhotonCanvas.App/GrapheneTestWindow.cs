using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The laser-induced graphene test (experimental): make the grid of strips, burn it, then type in
/// each strip's resistance pad to pad; the lowest is the best setting, kept in the library.
/// </summary>
public sealed class GrapheneTestWindow : Window
{
    readonly Document _doc;
    readonly MaterialLibrary _lib;
    readonly MachineProfile _machine;
    readonly TextBox _powers = new() { Text = "30, 45, 60, 75" }, _speeds = new() { Text = "50, 100, 200" };
    readonly NumericUpDown _defocus = Number(0, 10, 0.5m, "0.#"), _strip = Number(5, 50, 1, "0"), _width = Number(1, 10, 0.5m, "0.#");
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly StackPanel _rows = new() { Spacing = 4 };
    readonly TextBlock _best = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Cork 3 mm" };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    static string ReadingsFile => Path.Combine(AppIdentity.DataDir, "graphene-readings.json");
    readonly Dictionary<Guid, double> _ohms;

    public GrapheneTestWindow(Document doc, MaterialLibrary lib, MachineProfile machine, bool results)
    {
        _doc = doc;
        _lib = lib;
        _machine = machine;
        Title = "Graphene test (experimental)";
        Width = 700;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _defocus.Value = 1.5m; _strip.Value = 15; _width.Value = 3;
        _ohms = Load();
        foreach (var n in new[] { _defocus, _strip, _width }) n.ValueChanged += (_, _) => Plan();
        _powers.TextChanged += (_, _) => Plan();
        _speeds.TextChanged += (_, _) => Plan();
        var add = new Button { Content = "Add the test to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => Add();
        var make = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Laser-induced graphene: burning cork, wood or polyimide (Kapton) can turn its surface into conductive carbon, for touch pads and simple sensors. Whether the blue diode does it on your material is what this finds out: strips burned at different power and speed, each between two pads you measure across with a multimeter." },
                Field("Powers (%)", _powers, tip: "Across the grid, separated by commas."),
                Field("Speeds (mm/s)", _speeds, tip: "Down the grid, separated by commas."),
                Field("Focus raised (mm)", _defocus, tip: "Laser graphene usually wants a wider, softer spot than engraving: the head goes this much higher for the strips (and comes back after)."),
                Field("Strip length (mm)", _strip),
                Field("Strip width (mm)", _width),
                _plan,
                add,
                Hint("Cork and polyimide work best in papers; wood needs more trial. The burn should be black and velvety, not grey ash: too much power ashes it, too little leaves brown. Ventilate well."),
            },
        };
        _material.ItemsSource = lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
        _material.SelectionChanged += (_, _) => _newName.IsVisible = _material.SelectedIndex == 0;
        var save = new Button { Content = "Save the best to the library", Classes = { "primary" } };
        save.Click += (_, _) => Save();
        var read = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children =
            {
                Hint("Set the multimeter to resistance (Ω), press one probe on each pad of a strip, and type what it reads. Leave it empty if it reads nothing (open: no graphene)."),
                _rows,
                _best,
                Field("Material", _material),
                _newName,
                save,
                _status,
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Make the test", Content = new ScrollViewer { Content = make } }, new TabItem { Header = "Readings", Content = new ScrollViewer { Content = read } } } };
        tabs.SelectedIndex = results ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        Plan();
        Rows();
    }

    public static bool InDesign(DocState s) => GrapheneTest.Cells(s).Count > 0;

    static List<double> Parse(string? text) =>
        [.. (text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
            .Where(v => double.IsFinite(v) && v > 0)];

    GrapheneTestOptions Options() => new()
    {
        Powers = [.. Parse(_powers.Text).Select(v => Math.Min(100, v))],
        Speeds = [.. Parse(_speeds.Text)],
        DefocusMm = (double)(_defocus.Value ?? 1.5m),
        StripMm = (double)(_strip.Value ?? 15),
        StripWidthMm = (double)(_width.Value ?? 3),
    };

    void Plan()
    {
        var o = Options();
        int n = o.Powers.Count * o.Speeds.Count;
        double w = 14 + o.Powers.Count * (o.PadMm * 2 + o.StripMm + 6), h = 6 + o.Speeds.Count * (o.PadMm + 6);
        _plan.Text = n == 0 ? "Type at least one power and one speed." : $"{n} strips ({o.Powers.Count} powers × {o.Speeds.Count} speeds), about {w:0} × {h:0} mm, focus raised {o.DefocusMm:0.#} mm.";
    }

    void Add()
    {
        var o = Options();
        if (o.Powers.Count == 0 || o.Speeds.Count == 0) return;
        var (s, _) = GrapheneTest.Add(_doc.State, o with { Origin = new Vec2(10, 10) });
        _doc.Commit("Graphene test", s);
        AppLog.Write("graphene", $"Test added: {o.Powers.Count * o.Speeds.Count} strips.");
        Close();
    }

    void Rows()
    {
        _rows.Children.Clear();
        var cells = GrapheneTest.Cells(_doc.State);
        if (cells.Count == 0) { _rows.Children.Add(new TextBlock { Text = "There's no graphene test in this design: make one on the first tab." }); return; }
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var box = new TextBox { Width = 120, Watermark = "Ω", Text = _ohms.TryGetValue(cell.Id, out var v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "" };
            box.TextChanged += (_, _) =>
            {
                // "1500", "1.5k", "1.5 kΩ", "2M": k for thousands, M for millions.
                var t = (box.Text ?? "").Trim().Replace(",", ".").Replace("ohms", "").Replace("ohm", "").TrimEnd('Ω', 'ω', ' ');
                double mult = t.EndsWith('k') || t.EndsWith('K') ? 1e3 : t.EndsWith('M') ? 1e6 : 1;
                if (double.TryParse(t.TrimEnd('k', 'K', 'M', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var ohm) && ohm > 0) _ohms[cell.Id] = ohm * mult;
                else _ohms.Remove(cell.Id);
                Keep();
                ShowBest();
            };
            _rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new TextBlock { Text = $"{i + 1}.", Width = 28, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = $"{cell.Laser.Power:0.#}% at {cell.Laser.Speed:0.#} mm/s", Width = 180, VerticalAlignment = VerticalAlignment.Center }, box },
            });
        }
        ShowBest();
    }

    (Layer Cell, double Ohms, double PerSquare)? BestCell()
    {
        var cells = GrapheneTest.Cells(_doc.State);
        var o = Options();
        var best = GrapheneTest.Best([.. cells.Select(c => _ohms.TryGetValue(c.Id, out var v) ? v : (double?)null)], o);
        return best is { } b ? (cells[b.Index], b.Ohms, b.OhmsPerSquare) : null;
    }

    void ShowBest()
    {
        _best.Text = BestCell() is { } b
            ? $"Best: {b.Cell.Laser.Power:0.#}% at {b.Cell.Laser.Speed:0.#} mm/s, {Ohms(b.Ohms)} across the strip ({Ohms(b.PerSquare)} per square). Under about 100 Ω per square is good laser graphene; thousands is barely conductive."
            : "No readings yet.";
    }

    static string Ohms(double v) => v >= 1e6 ? $"{v / 1e6:0.##} MΩ" : v >= 1e3 ? $"{v / 1e3:0.##} kΩ" : $"{v:0.#} Ω";

    void Save()
    {
        if (BestCell() is not { } b) { _status.Text = "Type in at least one reading first."; return; }
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; return; }
            m = new Material { Name = name, Category = "Cork & rubber" };
        }
        else m = _lib.Materials[_material.SelectedIndex - 1];
        var recipe = new Recipe
        {
            Purpose = RecipePurpose.Engrave,
            Title = $"Graphene ({Ohms(b.PerSquare)} per square)",
            Settings = ProcessSettings.From(b.Cell),
            Tested = true,
            TestedOn = DateTime.Now,
            Notes = $"Laser-induced graphene: {Ohms(b.Ohms)} across a {Options().StripMm:0.#} × {Options().StripWidthMm:0.#} mm strip, focus raised {Math.Abs(b.Cell.FocusOffsetMm):0.#} mm.",
        };
        _lib.Upsert(m with { Recipes = m.Recipes.Add(recipe) });
        _lib.Save();
        _status.Text = $"Saved to {m.Label}.";
        _status.Foreground = Brush("Ok");
    }

    static Dictionary<Guid, double> Load()
    {
        try { return File.Exists(ReadingsFile) ? JsonSerializer.Deserialize<Dictionary<Guid, double>>(File.ReadAllText(ReadingsFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }

    void Keep()
    {
        try { Directory.CreateDirectory(AppIdentity.DataDir); File.WriteAllText(ReadingsFile, JsonSerializer.Serialize(_ohms)); }
        catch (IOException) { }
    }
}
