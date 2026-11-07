using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.App;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Flip;

/// <summary>
/// Flip pictures on metal: two to four selected pictures woven into one polished-stainless plate as
/// diffraction gratings, each with its own direction (or pitch), so each shows only when the plate
/// tilts its way. Shows the woven patches and a "tilt" preview before adding the layers.
/// </summary>
public sealed class FlipPictureWindow : Window
{
    readonly IReadOnlyList<ImageShape> _pictures;
    readonly MaterialLibrary _library;
    readonly MachineProfile _machine;
    readonly NumericUpDown _patch = Number(0.1m, 3, 0.05m, "0.##");
    readonly ComboBox _weave = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Tiles (checkerboard)", "Columns", "Rows", "Diagonal stripes" }, SelectedIndex = 0 };
    readonly NumericUpDown _power = Number(1, 100, 1, "0.#"), _speed = Number(2, 15000, 10, "0");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _spacing = Number(0.01m, 0.5m, 0.01m, "0.###");
    readonly ComboBox _fromTest = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<(GratingSet Set, string Label)> _tests = [];
    readonly ComboBox _tilt = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<ViewRow> _rows = [];
    readonly PreviewView _preview = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _testNote = Hint();
    readonly Avalonia.Threading.DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _loading;

    /// <summary>The options chosen, or null if cancelled.</summary>
    public FlipOptions? Result { get; private set; }

    /// <summary>One picture's settings.</summary>
    sealed class ViewRow
    {
        public readonly NumericUpDown Angle = Number(0, 179, 15, "0");
        public readonly NumericUpDown Pitch = Number(0.3m, 5, 0.05m, "0.00");
        public readonly NumericUpDown Threshold = Number(1, 99, 5, "0");
        public readonly NumericUpDown Strength = Number(5, 100, 5, "0");
        public readonly CheckBox Invert = new() { Content = "Mark the light parts" };
        public readonly CheckBox Tones = new() { Content = "Greys as tones" };
        public readonly TextBlock Tilt = Hint();
        public Control TargetRow = null!;
        public readonly ComboBox Target = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = GratingViews.All.Select(GratingViews.Short).ToList() };
        public double? Power;
    }

    public FlipPictureWindow(IReadOnlyList<ImageShape> pictures, MaterialLibrary library, MachineProfile machine)
    {
        _pictures = pictures;
        _machine = machine;
        _library = library;
        Title = "Flip picture (MOPA)";
        Width = 1060;
        Height = 760;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var d = new FlipOptions();
        _patch.Value = (decimal)d.PatchMm;
        _power.Value = (decimal)d.Power;
        _speed.Value = (decimal)d.Speed;
        _spacing.Value = (decimal)d.LineSpacingMm;
        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();
        _pulse.SelectedIndex = _machine.Mopa.ToList().FindIndex(m => m.PulseWidthNs == d.PulseWidthNs);

        int n = Math.Min(pictures.Count, FlipOptions.MaxViews);
        var angles = FlipOptions.SpreadAngles(n);
        var views = new StackPanel { Spacing = 8 };
        for (int i = 0; i < n; i++)
        {
            var row = new ViewRow();
            row.Angle.Value = (decimal)angles[i];
            row.Pitch.Value = 1.00m;
            row.Threshold.Value = 50;
            row.Strength.Value = 100;
            row.Target.SelectedIndex = DefaultTarget(angles[i]);
            _rows.Add(row);
            foreach (var x in new[] { row.Angle, row.Pitch, row.Threshold, row.Strength }) x.ValueChanged += (_, _) => Later();
            row.Invert.IsCheckedChanged += (_, _) => Later();
            row.Tones.IsCheckedChanged += (_, _) => Later();
            row.Target.SelectionChanged += (_, _) => ApplyTest();
            ToolTip.SetTip(row.Angle, "The scan direction for this picture's patches. It shows when the lamp and your eye are on a line along it. Keep the pictures' directions far apart (e.g. 0° and 90°).");
            ToolTip.SetTip(row.Pitch, "The pulse spacing. With the same direction, a different pitch shows at a different tilt or colour: use a grating test to pick one per side.");
            ToolTip.SetTip(row.Threshold, "Parts of the picture darker than this (0 black, 100 white) are marked and glint; lighter parts stay mirror.");
            ToolTip.SetTip(row.Strength, "Share of the marked patches kept. Lower it to make a faint, hidden picture (a serial number or QR code that only shows at one angle).");
            ToolTip.SetTip(row.Target, "With settings from a grating test: the view this picture should show from.");
            var name = Path.GetFileNameWithoutExtension(pictures[i].Name);
            views.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Swatch(i)),
                BorderThickness = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(10, 2, 0, 2),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = $"PICTURE {FlipPicture.Letter(i)}: {(string.IsNullOrWhiteSpace(name) ? "picture" : name)}", Classes = { "h" } },
                        Field("Direction °", row.Angle),
                        row.Tilt,
                        Field("Pitch µm", row.Pitch),
                        Field("Dark below", row.Threshold),
                        Field("Strength %", row.Strength),
                        new WrapPanel { Children = { row.Invert, new Border { Width = 12 }, row.Tones } },
                        (row.TargetRow = Field("Shows from", row.Target)),
                    },
                },
            });
        }

        // Grating tests saved in the material library (Measure colours → Gratings: five views).
        foreach (var m in library.Materials)
            foreach (var g in m.Gratings.Where(g => g.Views.Count > 0))
                _tests.Add((g, $"{m.Label}: {(string.IsNullOrWhiteSpace(g.Name) ? g.Measured.ToString("yyyy-MM-dd") : g.Name)}"));
        _fromTest.ItemsSource = new[] { "None: set directions and pitches by hand" }.Concat(_tests.Select(t => t.Label)).ToList();
        _fromTest.SelectedIndex = 0;
        _fromTest.IsEnabled = _tests.Count > 0;
        _fromTest.SelectionChanged += (_, _) => ApplyTest();
        ToolTip.SetTip(_fromTest, "Pick a grating test photographed from the five views (Measure colours, Gratings): each picture gets the pitch and power that showed most from its view and least from the others'.");

        _tilt.ItemsSource = new[] { "All pictures (the patches)" }.Concat(Enumerable.Range(0, n).Select(i => $"Tilted to show {FlipPicture.Letter(i)}")).ToList();
        _tilt.SelectedIndex = 0;
        _tilt.SelectionChanged += (_, _) => Update();

        foreach (var x in new[] { _patch, _power, _speed, _spacing }) x.ValueChanged += (_, _) => Later();
        _weave.SelectionChanged += (_, _) => Later();
        _pulse.SelectionChanged += (_, _) => Later();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Update(); };

        var add = new Button { Content = "Add flip layers", Classes = { "primary" }, IsDefault = true, MinWidth = 150 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        add.Click += (_, _) => { Result = Options(); Close(); };
        cancel.Click += (_, _) => Close();

        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Each picture becomes tiny diffraction gratings with its own direction, woven together in small patches. " +
                           "Tilt the plate one way and you see picture A, the other way picture B. The plate takes the place and size of picture A; the others are stretched to fit it.",
                },
                Field("Patch (mm)", _patch, tip: "Size of one patch. Each picture gets one patch in every 2–4, so smaller patches keep more detail but mean more stop-starts."),
                Field("Weave", _weave, tip: "How the pictures' patches are woven together. Tiles mix them most evenly; columns or rows can mark faster."),
                Field("Settings from test", _fromTest),
                _testNote,
                views,
                new TextBlock { Text = "MARKING", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                Field("Power %", _power, tip: "Keep it low: too much heat blurs the gratings and warps thin sheet."),
                Field("Speed mm/s", _speed, tip: "With the frequency, sets the pulse spacing (the pitch). Each layer's frequency is worked out from this."),
                Field("Pulse width", _pulse),
                Field("Line spacing mm", _spacing, tip: "Gap between fill lines. 0.05–0.06 mm covers the area with little heat."),
                _summary,
                Hint("Use polished (mirror) stainless held flat, focused. Each picture covers only part of the plate, so it is dimmer than a plain diffraction picture: try a small plate first."),
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, add } };
        var left = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(new ScrollViewer { Content = form });
        var right = new DockPanel();
        var tiltRow = Field("Preview", _tilt);
        DockPanel.SetDock(tiltRow, Dock.Top);
        right.Children.Add(tiltRow);
        right.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(6), Child = _preview, ClipToBounds = true, Margin = new(0, 8, 0, 0) });
        var grid = new Grid { ColumnDefinitions = new("400,16,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Content = new Border { Padding = new(16), Child = grid };
        ApplyTest();
        Update();
    }

    static Color Swatch(int i)
    {
        uint c = FlipPicture.Swatch(i);
        return Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }

    /// <summary>The view a direction is first read from: left/right for across, top/bottom for up-down.</summary>
    static int DefaultTarget(double angle)
    {
        double a = FlipPicture.Norm(angle);
        var v = a is >= 45 and < 135 ? GratingView.Top : GratingView.Left;
        return GratingViews.All.ToList().IndexOf(v);
    }

    /// <summary>Fill each picture's direction, pitch and power from the chosen grating test.</summary>
    void ApplyTest()
    {
        int k = _fromTest.SelectedIndex - 1;
        foreach (var r in _rows) r.TargetRow.IsVisible = k >= 0;
        if (k < 0 || k >= _tests.Count)
        {
            foreach (var r in _rows) r.Power = null;
            _testNote.Text = _tests.Count == 0 ? "No grating tests in the library yet: run Materials & Tests → Tests → Colour test (Diffraction gratings), then Measure colours in Gratings mode." : "";
            Later();
            return;
        }
        var targets = _rows.Select(r => GratingViews.All[Math.Max(0, r.Target.SelectedIndex)]).ToList();
        var s = FlipSuggest.FromGratings(_tests[k].Set, targets);
        if (s is null)
        {
            _testNote.Text = "That test has no measured squares.";
            return;
        }
        _loading = true;
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Angle.Value = (decimal)Math.Round(s[i].AngleDeg);
            _rows[i].Pitch.Value = (decimal)Math.Round(s[i].PitchUm, 2);
            _rows[i].Power = s[i].Power;
        }
        _speed.Value = (decimal)s[0].Speed;
        int pi = _machine.Mopa.ToList().FindIndex(m => m.PulseWidthNs == s[0].PulseWidthNs);
        if (pi >= 0) _pulse.SelectedIndex = pi;
        _loading = false;
        var weak = s.Where(x => x.Separation < 10).Select(x => FlipPicture.Letter(x.View)).ToList();
        _testNote.Text = "Directions, pitches and powers from the test." + (weak.Count == 0
            ? " Each picture's square stood out clearly from its view."
            : $" Picture{(weak.Count == 1 ? "" : "s")} {string.Join(", ", weak)} didn't stand out much from the others' views on that test: expect some bleed, or choose views further apart.")
            + (targets.Any(t => t is GratingView.Top or GratingView.Bottom) ? " Top and bottom are read from the test turned 90° (a good guess, not measured)." : "");
        Later();
    }

    void Later()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    FlipOptions Options() => new()
    {
        Views = [.. _rows.Select(r => new FlipView
        {
            AngleDeg = (double)(r.Angle.Value ?? 0),
            PitchUm = (double)(r.Pitch.Value ?? 1),
            Threshold = (double)(r.Threshold.Value ?? 50),
            StrengthPct = (double)(r.Strength.Value ?? 100),
            Invert = r.Invert.IsChecked == true,
            Tones = r.Tones.IsChecked == true,
            Power = r.Power,
        })],
        PatchMm = (double)(_patch.Value ?? 0.3m),
        Weave = _weave.SelectedIndex switch { 1 => FlipWeave.Columns, 2 => FlipWeave.Rows, 3 => FlipWeave.Diagonal, _ => FlipWeave.Tiles },
        Power = (double)(_power.Value ?? 32),
        Speed = (double)(_speed.Value ?? 300),
        PulseWidthNs = _machine.Mopa[Math.Max(0, _pulse.SelectedIndex)].PulseWidthNs,
        LineSpacingMm = (double)(_spacing.Value ?? 0.06m),
    };

    int _version;

    /// <summary>The marking time, worked out off the UI thread; the newest request wins.</summary>
    async Task EstimateAsync(FlipOptions o, string summary)
    {
        int v = ++_version;
        var pictures = _pictures;
        var machine = _machine;
        string time;
        try
        {
            time = await Task.Run(() =>
            {
                var s = DocState.New();
                var layer = Layer.Default(0, LayerMode.Image) with { Output = false };
                s = Ops.AddLayer(s, layer);
                var copies = pictures.Select(p => new ImageShape { Name = "p", LayerId = layer.Id, Source = p.Source, SizeMm = p.SizeMm, Transform = p.Transform }).ToList();
                s = Ops.AddShapes(s, copies);
                var (after, _) = FlipPicture.Add(s, copies, o, machine);
                return $" About {Duration(JobBuilder.Build(after, new JobOptions { WholeJobPasses = false }).EstimateS)} to mark.";
            });
        }
        catch (JobException e) { time = $" ⚠ {e.Message}"; }
        if (v == _version) _summary.Text = summary + time;
    }

    void Update()
    {
        var o = Options();
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Tilt.Text = $"Shows when you {FlipPicture.TiltHint(o.Views[i].AngleDeg)}.";
        var plate = _pictures[0];
        var map = FlipPicture.Map(_pictures.Select(p => p.Source.Pixels).ToList(), plate.SizeMm, o, _machine);
        _preview.Show(map, _tilt.SelectedIndex - 1);
        var parts = map.Buckets.Select(b => $"{FlipPicture.Letter(b.Index)} {b.Pixels} ({b.FrequencyKhz} kHz, {b.PitchUm:0.00} µm)");
        var clash = ClosestAngles(o);
        string warn = clash is { } c && c.Gap < 30 && !c.SamePitchOk
            ? $" ⚠ Pictures {c.A} and {c.B} are only {c.Gap:0}° apart with the same pitch: they will show together. Spread the directions or give them different pitches."
            : "";
        _summary.Text = map.Buckets.All(b => b.Pixels == 0)
            ? "Nothing to mark: every patch is left bare. Try a higher Dark below, or Mark the light parts."
            : $"{map.Cols} × {map.Rows} patches {map.PixelMm:0.##} mm. Marked: {string.Join(", ", parts)}." + warn;
        if (map.Buckets.Any(b => b.Pixels > 0))
        {
            var summary = _summary.Text;
            _summary.Text += " Working out the time…";
            _ = EstimateAsync(o, summary);
        }
    }

    /// <summary>The two pictures with the closest directions, and whether a pitch difference keeps them apart.</summary>
    static (string A, string B, double Gap, bool SamePitchOk)? ClosestAngles(FlipOptions o)
    {
        (string, string, double, bool)? best = null;
        for (int i = 0; i < o.Views.Count; i++)
            for (int j = i + 1; j < o.Views.Count; j++)
            {
                double d = Math.Abs(FlipPicture.Norm(o.Views[i].AngleDeg) - FlipPicture.Norm(o.Views[j].AngleDeg));
                d = Math.Min(d, 180 - d);
                bool pitchApart = Math.Abs(o.Views[i].PitchUm - o.Views[j].PitchUm) >= 0.15;
                if (best is null || d < best.Value.Item3) best = (FlipPicture.Letter(i), FlipPicture.Letter(j), d, pitchApart);
            }
        return best;
    }

    /// <summary>The woven patches in each picture's colour, or the plate as it looks tilted towards one picture.</summary>
    sealed class PreviewView : Control
    {
        GratingMap? _map;
        int _view = -1;

        public void Show(GratingMap map, int view)
        {
            _map = map;
            _view = view;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            if (_map is not { } map) return;
            double s = Math.Min((Bounds.Width - 20) / map.Cols, (Bounds.Height - 20) / map.Rows);
            double ox = (Bounds.Width - map.Cols * s) / 2, oy = (Bounds.Height - map.Rows * s) / 2;
            if (_view < 0)
            {
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#9DA3AC")), null, new Rect(ox, oy, map.Cols * s, map.Rows * s)); // bare steel
                var brushes = map.Buckets.Select(b => (IBrush)new SolidColorBrush(Swatch(b.Index))).ToList();
                for (int r = 0; r < map.Rows; r++)
                    for (int c = 0; c < map.Cols; c++)
                        if (map.Cells[r * map.Cols + c] is int k and >= 0)
                            ctx.DrawRectangle(brushes[k], null, new Rect(ox + c * s, oy + r * s, Math.Ceiling(s), Math.Ceiling(s)));
                return;
            }
            // Tilted: the chosen picture glints (in its rainbow-ish green), the others ghost faintly, the mirror is dark.
            var sim = FlipPicture.Simulate(map, _view);
            var shades = new Dictionary<byte, IBrush>();
            IBrush Shade(byte v) => shades.TryGetValue(v, out var b) ? b : shades[v] = new SolidColorBrush(v > 200 ? Color.FromRgb(120, (byte)v, 200) : Color.FromRgb(v, v, (byte)Math.Min(255, v + 8)));
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                    ctx.DrawRectangle(Shade(sim[r * map.Cols + c]), null, new Rect(ox + c * s, oy + r * s, Math.Ceiling(s), Math.Ceiling(s)));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            InvalidateVisual();
            return base.ArrangeOverride(finalSize);
        }
    }
}
