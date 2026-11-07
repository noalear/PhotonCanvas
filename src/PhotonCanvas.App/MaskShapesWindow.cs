using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>
/// Mask with shape: the selected shapes are cut to the inside (or outside) of one of them, the
/// mask. Lines are cut where they cross its edge, filled outlines to its shape. Previewed first.
/// </summary>
public sealed class MaskShapesWindow : Window
{
    readonly Document _doc;
    readonly List<PathShape> _candidates;
    readonly List<PathShape> _selected;
    readonly ComboBox _mask = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly RadioButton _inside = new() { Content = "Keep what's inside the mask", GroupName = "keep", IsChecked = true };
    readonly RadioButton _outside = new() { Content = "Keep what's outside the mask", GroupName = "keep" };
    /// <summary>What happens to the mask shape: onto the mask layer (never burned), left where it is, or deleted.</summary>
    readonly ComboBox _after = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "Moves to the mask layer (never burned)", "Stays on its own layer (burned with it)", "Is deleted" },
        SelectedIndex = 0,
    };
    const int AfterMaskLayer = 0, AfterStays = 1, AfterDeleted = 2;
    readonly Preview _preview = new() { Height = 460 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _ok = new() { Content = "Mask", Classes = { "primary" }, MinWidth = 120 };
    List<(PathShape Shape, PathGeometry Result)> _results = [];
    int _version;

    public MaskShapesWindow(Document doc, IReadOnlyList<PathShape> selected)
    {
        _doc = doc;
        _selected = [.. selected];
        _candidates = _selected.Where(p => p.World.Subpaths.Any(s => s.Closed)).ToList();
        Title = "Mask with shape";
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _mask.ItemsSource = _candidates.Select(Label).ToList();
        // The mask starts as the simplest closed shape (a frame or some lettering rather than a
        // thousand traced lines), the topmost of equals.
        var order = _doc.State.Shapes.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var first = _candidates.OrderBy(p => p.World.Subpaths.Count(s => s.Closed)).ThenByDescending(p => order.GetValueOrDefault(p.Id)).FirstOrDefault();
        _mask.SelectedIndex = first is null ? -1 : _candidates.IndexOf(first);
        _mask.SelectionChanged += (_, _) => Make();
        _inside.IsCheckedChanged += (_, _) => Make();
        _after.SelectionChanged += (_, _) => Make();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        _ok.Click += (_, _) => Apply();
        var side = new StackPanel
        {
            Spacing = 10,
            Width = 320,
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Cuts the other selected shapes to the mask's outline: everything past its edge is removed. Lines are cut where they cross the edge; filled shapes are cut to the mask's shape." },
                Field("Mask", _mask, tip: "The shape whose outline does the cutting. Only its closed outlines count."),
                new StackPanel { Spacing = 4, Children = { _inside, _outside } },
                Field("The mask afterwards", _after, tip: "Moves to the mask layer (default): kept on the work area to line things up with and to mask again, but never cut or marked. Stays: left on its layer, so it's cut or marked with it (a frame you also want cut). Is deleted: gone once it has done the cutting."),
                _summary,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _ok } },
                Hint("Text and codes aren't cut (they can be the mask). Pictures have their own Image mask on the Picture tab."),
            },
        };
        var body = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(side, Dock.Right);
        body.Children.Add(side);
        body.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(8), Margin = new(0, 0, 16, 0), Child = _preview });
        Content = body;
        Make();
    }

    static string Label(PathShape p)
    {
        var b = p.World.Bounds();
        string name = string.IsNullOrWhiteSpace(p.Name) ? "Shape" : p.Name;
        return $"{name} ({b.Width:0.#} × {b.Height:0.#} mm)";
    }

    PathShape? MaskShape => _mask.SelectedIndex >= 0 && _mask.SelectedIndex < _candidates.Count ? _candidates[_mask.SelectedIndex] : null;

    async void Make()
    {
        int v = ++_version;
        if (MaskShape is not { } mask)
        {
            _summary.Text = "Select a closed shape to mask with, as well as what to cut.";
            _ok.IsEnabled = false;
            return;
        }
        bool outside = _outside.IsChecked == true;
        var targets = _selected.Where(p => p.Id != mask.Id && p is not (TextShape or CodeShape)).ToList();
        _summary.Text = "Cutting…";
        var region = Mask.Region(mask.World);
        var results = await Task.Run(() => targets.Select(t => (t, Mask.Apply(t.World, region, outside))).ToList());
        if (v != _version) return;
        _results = results;
        _preview.Show(region, targets.Select(t => t.World).ToList(), results.Select(r => r.Item2).ToList());
        int gone = results.Count(r => r.Item2.IsEmpty);
        int before = targets.Sum(t => t.World.Subpaths.Length), after = results.Sum(r => r.Item2.Subpaths.Length);
        _summary.Text = targets.Count == 0 ? "Nothing to cut: select the shapes to mask as well as the mask."
            : $"Cuts {targets.Count} shape{(targets.Count == 1 ? "" : "s")} to the {(outside ? "outside" : "inside")} of \"{(string.IsNullOrWhiteSpace(mask.Name) ? "the mask" : mask.Name)}\": "
              + $"{before:N0} piece{(before == 1 ? "" : "s")} become {after:N0}"
              + (gone > 0 ? $", and {gone} shape{(gone == 1 ? " is" : "s are")} left with nothing and removed." : ".")
              + (_after.SelectedIndex switch
              {
                  AfterDeleted => " The mask is deleted.",
                  AfterStays => " The mask stays on its layer, so it's burned too.",
                  _ => $" The mask moves to the \"{MaskLayer.LayerName}\" layer: kept, but never burned.",
              });
        _ok.IsEnabled = targets.Count > 0;
    }

    void Apply()
    {
        if (MaskShape is not { } mask || _results.Count == 0) return;
        var s = _doc.State;
        var gone = new HashSet<Guid>();
        var kept = new List<Guid>();
        foreach (var (shape, result) in _results)
        {
            if (result.IsEmpty) { gone.Add(shape.Id); continue; }
            s = Ops.Replace(s, new PathShape { Id = shape.Id, Name = shape.Name, LayerId = shape.LayerId, GroupId = shape.GroupId, Geometry = result });
            kept.Add(shape.Id);
        }
        if (_after.SelectedIndex == AfterDeleted) gone.Add(mask.Id);
        if (gone.Count > 0) s = Ops.Delete(s, gone);
        if (_after.SelectedIndex == AfterMaskLayer) s = MaskLayer.Mark(s, new HashSet<Guid> { mask.Id });
        _doc.Commit("Mask with shape", s, kept);
        Close();
    }

    /// <summary>The shapes before (faint) and after (dark), with the mask's outline in blue.</summary>
    sealed class Preview : Control
    {
        PathGeometry _mask = PathGeometry.Empty;
        List<PathGeometry> _before = [], _after = [];
        Rect2 _area;

        public void Show(PathGeometry mask, List<PathGeometry> before, List<PathGeometry> after)
        {
            (_mask, _before, _after) = (mask, before, after);
            _area = before.Append(mask).Select(g => g.Bounds(0.05)).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
            InvalidateVisual();
        }

        static StreamGeometry Geo(PathGeometry g)
        {
            var sg = new StreamGeometry();
            using var c = sg.Open();
            c.SetFillRule(FillRule.EvenOdd);
            foreach (var f in g.Flatten(0.05))
            {
                if (f.Points.Count < 2) continue;
                c.BeginFigure(new Point(f.Points[0].X, f.Points[0].Y), f.Closed);
                for (int i = 1; i < f.Points.Count; i++) c.LineTo(new Point(f.Points[i].X, f.Points[i].Y));
                c.EndFigure(f.Closed);
            }
            return sg;
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#F4EFE6")), new Rect(Bounds.Size));
            if (_area.IsEmpty || _area.Width <= 0 || _area.Height <= 0) return;
            double k = Math.Min((Bounds.Width - 20) / _area.Width, (Bounds.Height - 20) / _area.Height);
            var m = Matrix.CreateTranslation(-_area.MinX, -_area.MinY) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation((Bounds.Width - _area.Width * k) / 2, (Bounds.Height - _area.Height * k) / 2);
            double px = 1 / k;
            using (ctx.PushTransform(m))
            {
                var faint = new SolidColorBrush(Color.FromArgb(50, 60, 40, 30));
                foreach (var g in _before) ctx.DrawGeometry(faint, new Pen(faint, px), Geo(g));
                var ink = new SolidColorBrush(Color.Parse("#3B2616"));
                foreach (var g in _after) ctx.DrawGeometry(ink, new Pen(ink, 1.2 * px), Geo(g));
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#2F6FED")), 1.6 * px, new DashStyle([4, 3], 0)), Geo(_mask));
            }
        }
    }
}
