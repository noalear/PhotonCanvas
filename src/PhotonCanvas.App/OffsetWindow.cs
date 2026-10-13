using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Offset the selected shapes: outward, inward or both, with sharp, round or
/// flat corners (and ends, for open lines). The result previews live on the
/// canvas; Cancel leaves the design untouched.
/// </summary>
public sealed class OffsetWindow : Window
{
    readonly Document _doc;
    readonly DocState _base;
    readonly List<PathShape> _sources;
    readonly NumericUpDown _distance = Number(0.01m, 200, 0.5m, "0.###");
    readonly ComboBox _direction = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Outward", "Inward", "Both sides" } };
    readonly ComboBox _corners = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Sharp", "Rounded", "Flat (cut off)" } };
    readonly ComboBox _ends = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Flat", "Rounded", "Square" } };
    readonly CheckBox _keep = new() { Content = "Keep the original shapes", IsChecked = true };
    readonly TextBlock _info = Hint();
    List<Guid> _newIds = [];
    bool _done;

    public OffsetWindow(Document doc, AppSettings settings)
    {
        _doc = doc;
        _base = doc.State;
        _sources = _base.Shapes.Where(s => doc.Selection.Contains(s.Id)).OfType<PathShape>().ToList();
        Title = "Offset";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _distance.Value = (decimal)settings.OffsetDistanceMm;
        _direction.SelectedIndex = Math.Clamp(settings.OffsetDirection, 0, 2);
        _corners.SelectedIndex = Math.Clamp(settings.OffsetCorners, 0, 2);
        _ends.SelectedIndex = Math.Clamp(settings.OffsetEnds, 0, 2);
        bool anyOpen = _sources.Any(s => s.World.Subpaths.Any(sp => !sp.Closed));
        var endsRow = Field("Line ends", _ends, tip: "For open lines (not closed shapes): how the ends of the outline are finished.");
        endsRow.IsVisible = anyOpen;

        var ok = new Button { Content = "Offset", IsDefault = true, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            _done = true;
            settings.OffsetDistanceMm = (double)(_distance.Value ?? 2);
            settings.OffsetDirection = _direction.SelectedIndex;
            settings.OffsetCorners = _corners.SelectedIndex;
            settings.OffsetEnds = _ends.SelectedIndex;
            settings.Save();
            _doc.EndEdit("Offset");
            _doc.Select(_newIds);
            Close();
        };
        cancel.Click += (_, _) => Close();
        Closed += (_, _) => { if (!_done) _doc.CancelEdit(); };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Field("Distance (mm)", _distance),
                    Field("Direction", _direction, tip: "Outward grows shapes (holes shrink); inward shrinks them. Both makes one of each."),
                    Field("Corners", _corners, tip: "Sharp keeps points sharp (very sharp points are trimmed so they don't spike out); rounded follows an arc; flat cuts them off."),
                    endsRow,
                    _keep,
                    _info,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                },
            },
        };
        foreach (var c in new[] { _direction, _corners, _ends }) c.SelectionChanged += (_, _) => Preview();
        _distance.ValueChanged += (_, _) => Preview();
        _keep.IsCheckedChanged += (_, _) => Preview();
        _doc.BeginEdit();
        Opened += (_, _) => Preview();
    }

    void Preview()
    {
        double d = (double)(_distance.Value ?? 0);
        var join = (JoinStyle)Math.Max(0, _corners.SelectedIndex);
        var end = (EndStyle)Math.Max(0, _ends.SelectedIndex);
        int dir = Math.Max(0, _direction.SelectedIndex);
        var made = new List<Shape>();
        int empty = 0;
        foreach (var src in _sources)
        {
            foreach (double sign in dir switch { 0 => new[] { 1.0 }, 1 => new[] { -1.0 }, _ => new[] { 1.0, -1.0 } })
            {
                var g = ShapeGeometry.Offset(src.World, sign * d, join, end);
                if (g.IsEmpty) { empty++; continue; }
                made.Add(new PathShape { Name = $"{(sign > 0 ? "Outset" : "Inset")} {src.Name}".Trim(), LayerId = src.LayerId, Geometry = g });
            }
        }
        var state = _base;
        if (_keep.IsChecked != true) state = Ops.Delete(state, _sources.Select(s => s.Id).ToHashSet());
        state = Ops.AddShapes(state, made);
        _newIds = made.Select(m => m.Id).ToList();
        _doc.UpdateEdit(state);
        _info.Text = _sources.Count == 0 ? "Select one or more vector shapes first (pictures can't be offset)."
            : empty > 0 ? $"{empty} result(s) vanished: the shape is thinner than twice the inward distance."
            : $"{made.Count} new shape{(made.Count == 1 ? "" : "s")}.";
    }
}
