using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Pack the selected parts onto the material: the work area, or any closed shape (e.g. an offcut picked from the camera).</summary>
public sealed class NestWindow : Window
{
    readonly Document _doc;
    readonly IReadOnlySet<Guid> _ids;
    readonly List<PathShape?> _sheets;
    readonly ComboBox _sheet = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _spacing = Number(0, 50, 0.5m, "0.##");
    readonly NumericUpDown _margin = Number(0, 50, 0.5m, "0.##");
    readonly ComboBox _rotation = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Don't rotate", "Quarter turns (90°)", "45° steps", "15° steps (slower)", "5° steps (slow)" }, SelectedIndex = 1 };
    readonly ComboBox _quality = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Normal (0.5 mm grid)", "Tight (0.25 mm grid, slower)", "Quick (1 mm grid)" }, SelectedIndex = 0 };
    readonly NumericUpDown _copies = Number(1, 500, 1, "0");
    readonly CheckBox _fill = new() { Content = "Fill the sheet with as many as fit" };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    CancellationTokenSource? _cts;

    public NestWindow(Document doc, IReadOnlySet<Guid> ids)
    {
        _doc = doc;
        _ids = Ops.WithGroups(doc.State, ids);
        Title = "Nest parts";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        // Any closed shape that isn't being nested can be the sheet.
        _sheets = [null];
        foreach (var s in doc.State.Shapes.OfType<PathShape>().Where(s => !_ids.Contains(s.Id) && s.Geometry.Subpaths.Any(sp => sp.Closed) && s.Bounds.Width * s.Bounds.Height > 100))
            _sheets.Add(s);
        _sheet.ItemsSource = _sheets.Select(s => s is null ? "The whole work area (220 × 220 mm)" :
            $"{(string.IsNullOrWhiteSpace(s.Name) ? "Shape" : s.Name)} · {s.Bounds.Width:0} × {s.Bounds.Height:0} mm").ToList();
        _sheet.SelectedIndex = 0;
        ToolTip.SetTip(_sheet, "Where the parts go. Pick a shape to nest inside it, for example an offcut outlined with Pick piece from camera.");
        _spacing.Value = 2;
        _margin.Value = 2;
        _copies.Value = 1;
        _fill.IsCheckedChanged += (_, _) => _copies.IsEnabled = _fill.IsChecked != true;

        int parts = doc.State.Shapes.Where(s => _ids.Contains(s.Id)).GroupBy(s => s.GroupId ?? s.Id).Count();
        var go = new Button { Content = "Nest", Classes = { "primary" }, IsDefault = true, MinWidth = 110 };
        go.Click += async (_, _) => await Run(go);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => { _cts?.Cancel(); Close(); };
        Closing += (_, _) => _cts?.Cancel();
        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"Packs {parts} part{(parts == 1 ? "" : "s")} as tightly as their real shapes allow. Groups stay together; anything already on the sheet is avoided." },
                    Field("Sheet", _sheet),
                    Field("Gap between", _spacing, tip: "Space kept between parts, mm. At least the cut width; more for fragile material."),
                    Field("Edge margin", _margin, tip: "Space kept from the sheet's edge, mm."),
                    Field("Rotation", _rotation),
                    Field("Packing", _quality),
                    Field("Copies of each", _copies, tip: "1 = just the parts you selected. More makes copies."),
                    _fill,
                    _busy,
                    _status,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, go } },
                },
            },
        };
    }

    async Task Run(Button go)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var opt = new NestOptions
        {
            Sheet = _sheets[Math.Max(0, _sheet.SelectedIndex)]?.World,
            SheetShapeId = _sheets[Math.Max(0, _sheet.SelectedIndex)]?.Id,
            SpacingMm = (double)(_spacing.Value ?? 2),
            MarginMm = (double)(_margin.Value ?? 2),
            RotationStepDeg = _rotation.SelectedIndex switch { 0 => 0, 1 => 90, 2 => 45, 3 => 15, _ => 5 },
            ResolutionMm = _quality.SelectedIndex switch { 1 => 0.25, 2 => 1, _ => 0.5 },
            Copies = (int)(_copies.Value ?? 1),
            FillSheet = _fill.IsChecked == true,
        };
        var state = _doc.State;
        go.IsEnabled = false;
        _busy.IsVisible = true;
        _status.Text = "Nesting…";
        try
        {
            var (next, report) = await Task.Run(() => Nesting.Nest(state, _ids, opt, MachineProfile.F2Ultra, cts.Token), cts.Token);
            if (!ReferenceEquals(state, _doc.State)) { _status.Text = "The design changed meanwhile; try again."; return; }
            var placedIds = _ids.Except(report.Unplaced).Concat(report.Added);
            _doc.Commit("Nest", next, report.Unplaced.Count > 0 ? report.Unplaced : placedIds);
            _status.Text = $"Placed {report.Placed} part{(report.Placed == 1 ? "" : "s")}" +
                (report.Added.Count > 0 ? $" ({report.Added.Count} new copies)" : "") +
                $", using about {report.SheetUsedPercent:0}% of the sheet." +
                (report.DidNotFit > 0 ? $" {report.DidNotFit} didn't fit and were left where they were (now selected)." : "") +
                " Ctrl+Z undoes it.";
        }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (ArgumentException e) { _status.Text = e.Message; }
        finally
        {
            _busy.IsVisible = false;
            go.IsEnabled = true;
        }
    }
}
