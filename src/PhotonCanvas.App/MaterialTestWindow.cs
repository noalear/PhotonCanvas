using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Makes a power × speed test grid. Burn it on a scrap of the material, pick
/// the square that looks best, and use its settings (each square's layer is
/// named after them).
/// </summary>
public sealed class MaterialTestWindow : Window
{
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Engrave (filled squares)", "Score (outlines)", "Cut (outlines)" }, SelectedIndex = 0 };
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Blue diode (wood, leather, acrylic, paper…)", "MOPA (metals, some plastics)" }, SelectedIndex = 0 };
    readonly NumericUpDown _pMin = Number(1, 100, 5, "0.#"), _pMax = Number(1, 100, 5, "0.#"), _pSteps = Number(1, 10, 1, "0");
    readonly NumericUpDown _sMin = Number(2, 15000, 10, "0.#"), _sMax = Number(2, 15000, 10, "0.#"), _sSteps = Number(1, 10, 1, "0");
    readonly NumericUpDown _cell = Number(3, 30, 1, "0.#");
    readonly TextBlock _summary = Hint();
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    readonly DocState _state;
    readonly string _font;

    /// <summary>The options chosen, or null if cancelled.</summary>
    public MaterialTestOptions? Result { get; private set; }

    public MaterialTestWindow(DocState state, string font)
    {
        _state = state;
        _font = font;
        Title = "Material test";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _pMin.Value = 10; _pMax.Value = 90; _pSteps.Value = 5;
        _sMin.Value = 50; _sMax.Value = 400; _sSteps.Value = 5;
        _cell.Value = 10;
        _mode.SelectionChanged += (_, _) => Defaults();
        _light.SelectionChanged += (_, _) => Defaults();
        foreach (var n in new[] { _pMin, _pMax, _pSteps, _sMin, _sMax, _sSteps, _cell }) n.ValueChanged += (_, _) => Update();

        var add = new Button { Content = "Add to design", Classes = { "primary" }, IsDefault = true, MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        add.Click += (_, _) => { Result = Options(); Close(); };
        cancel.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Not sure what power and speed to use? Burn this grid on a scrap of your material. " +
                               "Power goes up left to right, speed goes down top to bottom. Pick the square you like and use its numbers.",
                    },
                    Field("Test", _mode),
                    Field("Laser", _light),
                    Row(Field2("Power from %", _pMin), Field2("to", _pMax), Field2("steps", _pSteps)),
                    Row(Field2("Speed from", _sMin), Field2("to", _sMax), Field2("steps", _sSteps)),
                    Field("Square size (mm)", _cell),
                    _summary,
                    Hint("Each square gets its own layer named after its settings, so you can read them off afterwards. Labels go on a light \"Test labels\" layer."),
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, add } },
                },
            },
        };
        Update();
    }

    static Control Field2(string label, Control editor) => new StackPanel
    {
        Spacing = 2,
        Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 }, editor },
    };

    /// <summary>Sensible starting ranges for the chosen kind of test.</summary>
    void Defaults()
    {
        bool red = _light.SelectedIndex == 1;
        (decimal pMin, decimal pMax, decimal sMin, decimal sMax) = (_mode.SelectedIndex, red) switch
        {
            (0, false) => (10m, 90m, 50m, 400m),
            (0, true) => (20m, 100m, 200m, 2000m),
            (1, false) => (5m, 45m, 50m, 300m),
            (1, true) => (10m, 60m, 200m, 1500m),
            (_, false) => (50m, 100m, 3m, 25m),
            (_, true) => (60m, 100m, 20m, 200m),
        };
        _pMin.Value = pMin; _pMax.Value = pMax; _sMin.Value = sMin; _sMax.Value = sMax;
    }

    MaterialTestOptions Options() => new()
    {
        Mode = _mode.SelectedIndex switch { 0 => LayerMode.Fill, 1 => LayerMode.Score, _ => LayerMode.Cut },
        Light = _light.SelectedIndex == 1 ? LightSource.Red : LightSource.Blue,
        PowerMin = (double)(_pMin.Value ?? 10),
        PowerMax = (double)(_pMax.Value ?? 90),
        PowerSteps = (int)(_pSteps.Value ?? 5),
        SpeedMin = Math.Clamp((double)(_sMin.Value ?? 50), _machine.MinSpeed, _machine.MaxSpeed),
        SpeedMax = Math.Clamp((double)(_sMax.Value ?? 400), _machine.MinSpeed, _machine.MaxSpeed),
        SpeedSteps = (int)(_sSteps.Value ?? 5),
        CellMm = (double)(_cell.Value ?? 10),
        Font = _font,
    };

    void Update()
    {
        var o = Options();
        var b = MaterialTest.Bounds(_state, o);
        int n = o.PowerSteps * o.SpeedSteps;
        bool fits = b.MaxX <= _machine.FieldW && b.MaxY <= _machine.FieldH;
        _summary.Text = $"{n} squares, about {b.MaxX - b.MinX:0} × {b.MaxY - b.MinY:0} mm, placed at the top-left of the work area." +
                        (fits ? "" : " ⚠ That's bigger than the work area: use fewer steps or smaller squares.");
    }
}
