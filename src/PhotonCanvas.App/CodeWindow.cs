using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Make or change a QR code, Data Matrix or barcode: what it says (fields allowed), its kind,
/// module size and margin, with a live preview of what the next run marks.
/// </summary>
public sealed class CodeWindow : Window
{
    static readonly CodeKind[] Kinds = [CodeKind.QR, CodeKind.DataMatrix, CodeKind.Code128, CodeKind.Code39, CodeKind.Ean13, CodeKind.UpcA];

    readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Kinds.Select(CodeShape.KindName).ToList() };
    readonly TextBox _content = new() { AcceptsReturn = false, Watermark = "https://… or Part {serial:0000}" };
    readonly ComboBox _ecc = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "L · 7% can be damaged", "M · 15% (usual)", "Q · 25%", "H · 30% (rough surfaces)" } };
    readonly NumericUpDown _module = Number(0.05m, 10, 0.05m, "0.###"), _barHeight = Number(1, 200, 1, "0.#"), _quiet = Number(0, 20, 1, "0");
    readonly CheckBox _invert = new() { Content = "Mark the light parts (inverted)" };
    readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 260 };
    readonly TextBlock _info = Hint();
    readonly Control _eccRow, _barRow;
    readonly VariableData _vars;

    /// <summary>The settings chosen, or null if cancelled.</summary>
    public CodeSpec? Result { get; private set; }

    public CodeWindow(CodeSpec? current, VariableData vars, Action? openVariables)
    {
        _vars = vars;
        var spec = current ?? new CodeSpec(CodeKind.QR, "{serial}");
        Title = current is null ? "Add a code" : "Edit code";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _kind.SelectedIndex = Array.IndexOf(Kinds, spec.Kind);
        _content.Text = spec.Content;
        _ecc.SelectedIndex = (int)spec.Ecc;
        _module.Value = (decimal)spec.ModuleMm;
        _barHeight.Value = (decimal)spec.BarHeightMm;
        _quiet.Value = spec.QuietModules;
        _invert.IsChecked = spec.Invert;
        ToolTip.SetTip(_module, "Size of one square (QR, Data Matrix) or the narrowest bar. Phones read QR codes down to about 0.3 mm squares on a clean mark; bigger is safer on wood.");
        ToolTip.SetTip(_quiet, "Clear margin around the code, in modules. Scanners want 4 (QR), 1 (Data Matrix) or 10 (barcodes) unless the material around it is plain anyway. When inverted, the margin is marked.");
        ToolTip.SetTip(_invert, "For materials that mark light on dark (anodised aluminium, slate, painted metal): the code's light parts are marked, so it reads the right way round.");

        _eccRow = Field("Error correction", _ecc);
        _barRow = Above("Bar height mm", _barHeight);
        foreach (var n in new[] { _module, _barHeight, _quiet })
        {
            n.ShowButtonSpinner = false;
            n.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        _kind.SelectionChanged += (_, _) => Update();
        _content.TextChanged += (_, _) => Update();
        _ecc.SelectionChanged += (_, _) => Update();
        foreach (var n in new[] { _module, _barHeight, _quiet }) n.ValueChanged += (_, _) => Update();
        _invert.IsCheckedChanged += (_, _) => Update();

        var ok = new Button { Content = current is null ? "Add code" : "Save", Classes = { "primary" }, IsDefault = true, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        ok.Click += (_, _) => { Result = Spec(); Close(); };
        cancel.Click += (_, _) => Close();
        RenderOptions.SetBitmapInterpolationMode(_preview, Avalonia.Media.Imaging.BitmapInterpolationMode.None);

        var form = new StackPanel
        {
            Spacing = 8,
            Width = 340,
            Children =
            {
                Field("Kind", _kind),
                Field("What it says", _content),
                FieldMenu.Create(_content, () => _vars, openVariables),
                _eccRow,
                Row(Above("Module mm", _module), Above("Margin (modules)", _quiet), _barRow),
                _invert,
                Hint("Put it on an Engrave layer. Fine lines (high lines/cm) give the crispest squares."),
            },
        };
        var right = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "NEXT RUN MARKS", Classes = { "h" } },
                new Border { Background = Brushes.White, CornerRadius = new(6), Padding = new(12), Child = _preview, MinHeight = 120 },
                _info,
            },
        };
        var grid = new Grid { ColumnDefinitions = new("Auto,20,*") };
        grid.Children.Add(form);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    grid,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                },
            },
        };
        Update();
    }

    static Control Above(string label, Control editor) => new StackPanel
    {
        Spacing = 2,
        Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 }, editor },
    };

    CodeSpec Spec() => new(
        Kinds[Math.Max(0, _kind.SelectedIndex)],
        _content.Text ?? "",
        (double)(_module.Value ?? 0.5m),
        (double)(_barHeight.Value ?? 12),
        _invert.IsChecked == true,
        (int)(_quiet.Value ?? 0),
        (CodeEcc)Math.Max(0, _ecc.SelectedIndex));

    void Update()
    {
        var spec = Spec();
        _eccRow.IsVisible = spec.Kind == CodeKind.QR;
        _barRow.IsVisible = spec.IsBarcode;
        string text = VariableText.Resolve(spec.Content, _vars, DateTime.Now);
        var (geometry, error) = CodeShape.Build(spec, text);
        var old = _preview.Source as IDisposable;
        _preview.Source = null;
        old?.Dispose();
        if (error is not null)
        {
            _info.Text = $"⚠ {error}";
            _info.Foreground = Brush("Warn");
            return;
        }
        _info.Foreground = Brush("TextMuted");
        var (cols, rows, dark) = CodeShape.Encode(spec.Kind, text, spec.Ecc);
        int q = spec.Invert ? Math.Max(1, spec.QuietModules) : spec.QuietModules, qy = spec.IsBarcode ? 0 : q;
        // One pixel per module (bars a fixed height), scaled up crisply by the preview.
        int barPx = spec.IsBarcode ? Math.Max(8, (int)Math.Round(spec.BarHeightMm / Math.Max(0.01, spec.ModuleMm))) : 1;
        int w = cols + 2 * q, h = rows * barPx + 2 * qy;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int cx = x - q, cy = (y - qy) / barPx;
                bool inCode = cx >= 0 && cx < cols && y >= qy && y < qy + rows * barPx;
                bool isDark = inCode && dark[cy * cols + cx];
                bool marked = spec.Invert ? !isDark : isDark;
                byte v = marked ? (byte)20 : (byte)245;
                int i = (y * w + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                rgba[i + 3] = 255;
            }
        using (var ms = new MemoryStream(new RasterImage(w, h, rgba).EncodePng()))
            _preview.Source = new Avalonia.Media.Imaging.Bitmap(ms);
        var b = geometry.Flatten(0.01).SelectMany(f => f.Points).ToList();
        var box = PhotonCanvas.Core.Rect2.Bounds(b);
        _info.Text = (text != spec.Content ? $"“{text}” · " : "") + $"{cols} × {rows} modules, {box.Width:0.#} × {box.Height:0.#} mm.";
    }
}
