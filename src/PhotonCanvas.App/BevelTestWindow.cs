using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The bevel test: how far the galvo's beam slants across the field. Four bars are cut through a
/// board; their lengths and their holes' lengths, measured with calipers, give the mirrors' pivot
/// heights, and from them the bevel any edge gets where it lies.
/// </summary>
public sealed class BevelTestWindow : Window
{
    readonly Document _doc;
    readonly AppSettings _settings;
    readonly MachineProfile _machine;
    readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<(string Label, Layer Layer, double? Thickness)> _sources = [];
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBox _thickness = Box("e.g. 6.1");
    readonly TextBox _longBarX = Box("bar"), _longHoleX = Box("hole"), _shortBarX = Box("bar"), _shortHoleX = Box("hole");
    readonly TextBox _longBarY = Box("bar"), _longHoleY = Box("hole"), _shortBarY = Box("bar"), _shortHoleY = Box("hole");
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    double? _pivotX, _pivotY;

    public BevelTestWindow(Document doc, MaterialLibrary lib, AppSettings settings, MachineProfile machine, bool results)
    {
        _doc = doc;
        _settings = settings;
        _machine = machine;
        Title = "Bevel test";
        Width = 700;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        foreach (var l in doc.State.Layers.Where(l => l.Mode == LayerMode.Cut && l.Name != Bevel.TestLayer))
            _sources.Add(($"{l.Name} · {l.Laser.Power:0.#}% {l.Laser.Speed:0.#} mm/s × {l.Laser.Passes}", l, null));
        foreach (var m in lib.Materials)
            foreach (var r in m.Recipes.Where(r => r.Purpose == RecipePurpose.Cut))
                _sources.Add(($"{m.Label} · {r.Title}", r.Settings.ApplyTo(Layer.Default(0, LayerMode.Cut)), m.ThicknessMm));
        _source.ItemsSource = _sources.Select(x => x.Label).ToList();
        // A library cut knows its board's thickness: filled in when chosen (also the first one, chosen to start with).
        _source.SelectionChanged += (_, _) =>
        {
            if (_sources.ElementAtOrDefault(_source.SelectedIndex).Thickness is { } t && string.IsNullOrWhiteSpace(_thickness.Text))
                _thickness.Text = t.ToString("0.##", CultureInfo.InvariantCulture);
        };
        _source.SelectedIndex = _sources.Count > 0 ? 0 : -1;
        var (longMm, shortMm) = Bevel.BarLengths(machine);
        _plan.Text = _sources.Count == 0
            ? "There's nothing to cut it with yet: add a cut layer with settings that go right through your board (the Cut test finds them), or save a cut to the library."
            : $"Four bars 12 mm wide, {longMm:0} and {shortMm:0} mm long, two across and two down, in the bottom right of the bed. Use a board at least 4 mm thick (thicker shows more): the slope is a fraction of a millimetre per millimetre of thickness.";
        var add = new Button { Content = "Add the test to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = _sources.Count > 0 };
        add.Click += (_, _) => Add();
        var make = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "A galvo's beam comes down at a slant away from the middle of the field, so cut edges there slope instead of standing upright. Edge by edge, that slope can be used for bevels (pyramids, chamfered boxes, sloped stacked slices) — once it's known how much it slants. This test measures it: the bars' ends lie at different distances from the middle, so the long bars come out longer at the bottom than at the top by more than the short ones do." },
                Field("Cut with", _source, tip: "Settings that cut right through the board, cleanly: the bars must drop out."),
                _plan,
                add,
                Hint("Cut it, let the bars drop out and leave the board where it is until you've measured the holes."),
            },
        };

        foreach (var b in new[] { _thickness, _longBarX, _longHoleX, _shortBarX, _shortHoleX, _longBarY, _longHoleY, _shortBarY, _shortHoleY })
            b.TextChanged += (_, _) => Work();
        var save = new Button { Content = "Keep these for bevel cuts", Classes = { "primary" } };
        save.Click += (_, _) => Save();
        var read = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children =
            {
                Hint("With calipers, in mm: each bar's length with the outside jaws (they close on its bottom face, the widest), and the length of the hole it left with the inside jaws, from the top of the board (its top opening, the narrowest). Clear the ash off the edges first; two decimals if your calipers show them."),
                Field("Board thickness", _thickness),
                Pair($"Long bar across ({longMm:0} mm)", _longBarX, _longHoleX),
                Pair($"Short bar across ({shortMm:0} mm)", _shortBarX, _shortHoleX),
                Pair($"Long bar down ({longMm:0} mm)", _longBarY, _longHoleY),
                Pair($"Short bar down ({shortMm:0} mm)", _shortBarY, _shortHoleY),
                _result,
                save,
                _status,
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Make the test", Content = new ScrollViewer { Content = make } }, new TabItem { Header = "Measurements", Content = new ScrollViewer { Content = read } } } };
        tabs.SelectedIndex = results ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        Work();
    }

    static TextBox Box(string watermark) => new() { Width = 110, Watermark = watermark };

    static Control Pair(string label, TextBox bar, TextBox hole) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 10,
        Children = { new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center }, bar, hole },
    };

    static double? Read(TextBox b) =>
        double.TryParse((b.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    void Add()
    {
        if (_sources.ElementAtOrDefault(_source.SelectedIndex).Layer is not { } cut) return;
        _doc.Commit("Bevel test", Bevel.AddTest(_doc.State, _machine, cut));
        AppLog.Write("bevel", $"Test added, cut at {cut.Laser.Power:0.#}% {cut.Laser.Speed:0.#} mm/s × {cut.Laser.Passes}.");
        Close();
    }

    void Work()
    {
        var (longMm, shortMm) = Bevel.BarLengths(_machine);
        double? Pivot(TextBox lb, TextBox lh, TextBox sb, TextBox sh) =>
            Read(_thickness) is { } t && Read(lb) is { } a && Read(lh) is { } b && Read(sb) is { } c && Read(sh) is { } d
                ? Bevel.PivotFromBars(t, longMm, a, b, shortMm, c, d) : null;
        _pivotX = Pivot(_longBarX, _longHoleX, _shortBarX, _shortHoleX);
        _pivotY = Pivot(_longBarY, _longHoleY, _shortBarY, _shortHoleY);
        string Say(string what, double? h) => h is { } p
            ? $"{what}: the beam pivots {p:0} mm above the material. An edge at the side of the field slopes {Bevel.MaxAngle(_machine, p):0.#}°; {Bevel.DistanceFor(5, p):0} mm out from the middle gives 5°, {Bevel.DistanceFor(10, p):0} mm gives 10°."
            : $"{what}: type in both bars and both holes.";
        _result.Text = Say("Across", _pivotX) + "\n" + Say("Down", _pivotY);
        // The two mirrors are a few centimetres apart, so the two heights should be close; far apart (or tiny) is a misreading.
        if (_pivotX is { } x && _pivotY is { } y && (Math.Abs(x - y) > 0.25 * Math.Max(x, y) || Math.Min(x, y) < 80))
            _result.Text += "\n⚠ These don't look right: the two should be within a few centimetres of each other, a few hundred millimetres. Measure again, pressing the jaws square on the edges.";
        else if (Read(_thickness) is null && _thickness.Text?.Length > 0)
            _result.Text += "\n⚠ Type the board's thickness in mm.";
    }

    void Save()
    {
        if (_pivotX is null && _pivotY is null) { _status.Text = "Type in the measurements first."; return; }
        // One direction measured: the other is close enough to start with.
        _settings.BevelPivotAcross = _pivotX ?? _pivotY;
        _settings.BevelPivotDown = _pivotY ?? _pivotX;
        _settings.Save();
        AppLog.Write("bevel", $"Pivot heights kept: across {_settings.BevelPivotAcross:0} mm, down {_settings.BevelPivotDown:0} mm.");
        _status.Text = "Kept.";
        _status.Foreground = Brush("Ok");
    }
}
