using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Turns a picture into diffraction-grating pixels for the MOPA laser on polished stainless:
/// brightness as grating direction (it shimmers as it tilts) or hue as grating pitch (true-ish
/// colours from one viewing angle). Shows the pixel map before adding the layers.
/// </summary>
public sealed class DiffractionWindow : Window
{
    readonly ImageShape _picture;
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Shimmer: brightness → direction", "Colour: hue → pitch" }, SelectedIndex = 0 };
    readonly NumericUpDown _pixel = Number(0.1m, 5, 0.1m, "0.##");
    readonly NumericUpDown _angleSteps = Number(2, 36, 1, "0"), _colourSteps = Number(2, 24, 1, "0");
    readonly NumericUpDown _colourAngle = Number(0, 179, 15, "0");
    readonly NumericUpDown _greenPitch = Number(0.3m, 5, 0.05m, "0.00");
    readonly CheckBox _skipLight = new() { Content = "Leave white unmarked (bare metal)", IsChecked = true };
    readonly CheckBox _skipGrey = new() { Content = "Leave greys and black unmarked", IsChecked = true };
    readonly NumericUpDown _power = Number(1, 100, 1, "0.#"), _speed = Number(2, 15000, 10, "0");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _spacing = Number(0.01m, 0.5m, 0.01m, "0.###");
    readonly Control _angleRow, _colourRows, _skipGreyRow;
    readonly PreviewView _preview = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly Avalonia.Threading.DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>The options chosen, or null if cancelled.</summary>
    public DiffractionOptions? Result { get; private set; }

    public DiffractionWindow(ImageShape picture)
    {
        _picture = picture;
        Title = "Diffraction picture (MOPA)";
        Width = 1000;
        Height = 700;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var d = new DiffractionOptions();
        _pixel.Value = (decimal)d.PixelMm;
        _angleSteps.Value = d.AngleSteps;
        _colourSteps.Value = d.ColourSteps;
        _colourAngle.Value = (decimal)d.ColourAngleDeg;
        _greenPitch.Value = (decimal)d.GreenPitchUm;
        _power.Value = (decimal)d.Power;
        _speed.Value = (decimal)d.Speed;
        _spacing.Value = (decimal)d.LineSpacingMm;
        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();
        _pulse.SelectedIndex = _machine.Mopa.ToList().FindIndex(m => m.PulseWidthNs == d.PulseWidthNs);

        _angleRow = Field("Directions", _angleSteps, tip: "How many grating directions brightness is shared out over (0–180°). More = smoother shimmer, more layers.");
        _colourRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Colours", _colourSteps, tip: "How many pitches the hues are shared out over (red to violet)."),
                Field("Direction °", _colourAngle, tip: "The scan direction for every pixel: the picture shows its colours when the light and your eye line up along it."),
            },
        };
        _skipGreyRow = _skipGrey;
        ToolTip.SetTip(_greenPitch, "Pulse spacing that shows green where you'll look from. Smaller pitches show bluer, larger redder. Find yours with Laser → Colour test → Diffraction gratings.");
        foreach (var n in new[] { _pixel, _angleSteps, _colourSteps, _colourAngle, _greenPitch, _power, _speed, _spacing }) n.ValueChanged += (_, _) => Later();
        _mode.SelectionChanged += (_, _) => Later();
        _pulse.SelectionChanged += (_, _) => Later();
        _skipLight.IsCheckedChanged += (_, _) => Later();
        _skipGrey.IsCheckedChanged += (_, _) => Later();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Update(); };

        var add = new Button { Content = "Add grating layers", Classes = { "primary" }, IsDefault = true, MinWidth = 150 };
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
                    Text = "Each pixel becomes a tiny diffraction grating: the laser's pulses land a set distance apart (speed ÷ frequency) and split light into rainbow colours. " +
                           "Shimmer uses the grating's direction, so parts light up as you tilt it. Colour uses its pitch, for colours that show from one angle.",
                },
                Field("Mode", _mode),
                Field("Pixel (mm)", _pixel, tip: "Size of one grating pixel. Smaller = more detail, more stop-starts."),
                _angleRow,
                _colourRows,
                Field("Pitch for green µm", _greenPitch),
                _skipLight,
                _skipGreyRow,
                new TextBlock { Text = "MARKING", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                Field("Power %", _power, tip: "Keep it low: too much heat blurs the grating and warps thin sheet."),
                Field("Speed mm/s", _speed, tip: "With the frequency, sets the pulse spacing (the pitch). Each pixel's frequency is worked out from this."),
                Field("Pulse width", _pulse),
                Field("Line spacing mm", _spacing, tip: "Gap between fill lines. 0.05–0.06 mm covers the area with little heat."),
                _summary,
                Hint("Use polished (mirror) stainless held flat, focused. Run a grating test first to find the power and the pitch that show green for you."),
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, add } };
        var left = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(new ScrollViewer { Content = form });
        var grid = new Grid { ColumnDefinitions = new("380,16,*") };
        grid.Children.Add(left);
        var pv = new Border { Background = Brush("Bg0"), CornerRadius = new(6), Child = _preview, ClipToBounds = true };
        Grid.SetColumn(pv, 2);
        grid.Children.Add(pv);
        Content = new Border { Padding = new(16), Child = grid };
        Update();
    }

    void Later()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    DiffractionOptions Options() => new()
    {
        Mode = _mode.SelectedIndex == 1 ? DiffractionMode.Colour : DiffractionMode.Shimmer,
        PixelMm = (double)(_pixel.Value ?? 0.5m),
        AngleSteps = (int)(_angleSteps.Value ?? 12),
        ColourSteps = (int)(_colourSteps.Value ?? 10),
        ColourAngleDeg = (double)(_colourAngle.Value ?? 0),
        GreenPitchUm = (double)(_greenPitch.Value ?? 1),
        SkipLight = _skipLight.IsChecked == true,
        SkipGrey = _skipGrey.IsChecked == true,
        Power = (double)(_power.Value ?? 32),
        Speed = (double)(_speed.Value ?? 300),
        PulseWidthNs = _machine.Mopa[Math.Max(0, _pulse.SelectedIndex)].PulseWidthNs,
        LineSpacingMm = (double)(_spacing.Value ?? 0.06m),
    };

    void Update()
    {
        var o = Options();
        _angleRow.IsVisible = o.Mode == DiffractionMode.Shimmer;
        _colourRows.IsVisible = _skipGreyRow.IsVisible = o.Mode == DiffractionMode.Colour;
        var map = Diffraction.Map(_picture.Source.Pixels, _picture.SizeMm, o, _machine);
        _preview.Show(map, o.Mode);
        int marked = map.Cells.Count(c => c >= 0);
        string time = "";
        try
        {
            var s = DocState.New();
            var layer = Layer.Default(0, LayerMode.Image);
            s = Ops.AddLayer(s, layer);
            var copy = new ImageShape { Name = "p", LayerId = layer.Id, Source = _picture.Source, SizeMm = _picture.SizeMm, Transform = _picture.Transform };
            s = Ops.AddShapes(s, [copy]);
            var (after, _) = Diffraction.Add(s, copy, o, _machine);
            after = Ops.UpdateLayers(after, after.Layers.Where(l => !l.Name.StartsWith(Diffraction.LayerPrefix)).Select(l => l.Id).ToHashSet(), l => l with { Output = false });
            time = $" About {Duration(JobBuilder.Build(after, new JobOptions { WholeJobPasses = false }).EstimateS)} to mark.";
        }
        catch (JobException e) { time = $" ⚠ {e.Message}"; }
        var khz = map.Buckets.Select(b => b.FrequencyKhz).DefaultIfEmpty(0).ToList();
        _summary.Text = map.Buckets.Count == 0
            ? "Nothing to mark: every pixel is left bare. Try turning off \"Leave white unmarked\", or the other mode."
            : $"{map.Cols} × {map.Rows} pixels, {marked} marked, as {map.Buckets.Count} layer{(map.Buckets.Count == 1 ? "" : "s")} " +
              $"({khz.Min()}–{khz.Max()} kHz, pitch {map.Buckets.Min(b => b.PitchUm):0.00}–{map.Buckets.Max(b => b.PitchUm):0.00} µm).{time}";
    }

    /// <summary>The pixel map: shimmer pixels coloured by direction (a colour wheel), colour pixels by the picture's colour.</summary>
    sealed class PreviewView : Control
    {
        GratingMap? _map;
        DiffractionMode _mode;

        public void Show(GratingMap map, DiffractionMode mode)
        {
            _map = map;
            _mode = mode;
            InvalidateVisual();
        }

        static Color Wheel(double deg)
        {
            double h = deg / 180 * 360 / 60, c = 0.75, x = c * (1 - Math.Abs(h % 2 - 1)), m = 0.2;
            var (r, g, b) = ((int)h % 6) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            if (_map is not { } map) return;
            double s = Math.Min((Bounds.Width - 20) / map.Cols, (Bounds.Height - 20) / map.Rows);
            double ox = (Bounds.Width - map.Cols * s) / 2, oy = (Bounds.Height - map.Rows * s) / 2;
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#9DA3AC")), null, new Rect(ox, oy, map.Cols * s, map.Rows * s)); // bare steel
            var brushes = map.Buckets.Select(b => (IBrush)new SolidColorBrush(_mode == DiffractionMode.Shimmer
                ? Wheel(b.AngleDeg)
                : Color.FromRgb((byte)(b.Preview >> 16), (byte)(b.Preview >> 8), (byte)b.Preview))).ToList();
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                {
                    int k = map.Cells[r * map.Cols + c];
                    if (k < 0) continue;
                    ctx.DrawRectangle(brushes[k], null, new Rect(ox + c * s, oy + r * s, Math.Ceiling(s), Math.Ceiling(s)));
                }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            InvalidateVisual();
            return base.ArrangeOverride(finalSize);
        }
    }
}
