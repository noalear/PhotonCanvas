using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>
/// A picture redrawn as engraving lines, banknote style: lines that swell in the shadows and thin
/// away in the light, straight, bent to the picture's forms, in rings or a spiral. Previewed live;
/// added as a fill layer of ribbons.
/// </summary>
public sealed class EngravingWindow : Window
{
    readonly Document _doc;
    readonly ImageShape _picture;
    readonly ComboBox _style = new() { ItemsSource = new[] { "Straight lines", "Lines following the forms", "Rings", "Spiral" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _spacing = Number(0.2m, 5, 0.05m, "0.0#"), _width = Number(10, 100, 5, "0"), _angle = Number(-180, 180, 15, "0"), _bend = Number(0, 5, 0.25m, "0.0#");
    readonly Slider _cx = new() { Minimum = 0, Maximum = 1, Value = 0.5, Width = 160 }, _cy = new() { Minimum = 0, Maximum = 1, Value = 0.45, Width = 160 };
    readonly CheckBox _hidePicture = new() { Content = "Don't engrave the picture itself (its layer off)", IsChecked = true };
    readonly Preview _preview = new() { Height = 420 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(250) };
    Control _angleRow = null!, _bendRow = null!, _centreRow = null!;
    PathGeometry _lines = PathGeometry.Empty;

    public EngravingWindow(Document doc, ImageShape picture)
    {
        _doc = doc;
        _picture = picture;
        Title = "Engraving lines";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _spacing.Value = 0.6m; _width.Value = 85; _angle.Value = 30; _bend.Value = 1m;
        foreach (var n in new[] { _spacing, _width, _angle, _bend }) n.ValueChanged += (_, _) => Later();
        _style.SelectionChanged += (_, _) => { Rows(); Later(); };
        _cx.ValueChanged += (_, _) => Later();
        _cy.ValueChanged += (_, _) => Later();
        _later.Tick += (_, _) => { _later.Stop(); Make(); };
        var add = new Button { Content = "Add to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => Add();
        _angleRow = Field("Angle (°)", _angle, tip: "The lines' direction: 0 = left to right.");
        _bendRow = Field("Bend", _bend, tip: "How far the lines bend with the picture's light and dark, in line spacings: more gives a stronger relief.");
        _centreRow = Field("Centre", new StackPanel { Spacing = 4, Children = { _cx, _cy } }, tip: "Where the rings or spiral start: across, then down the picture.");
        var side = new StackPanel
        {
            Spacing = 10, Width = 320,
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "The picture as engraving lines, like a banknote portrait: each line swells where the picture is dark and thins away to nothing where it's light." },
                Field("Lines", _style),
                Field("Apart (mm)", _spacing, tip: "Centre to centre. Closer lines show more detail; at least twice the beam's width so they stay apart."),
                Field("Darkest width (%)", _width, tip: "How wide a line gets at black, as a share of the spacing. Below 100% the lines never quite touch."),
                _angleRow, _bendRow, _centreRow,
                _hidePicture,
                _summary,
                add,
                Hint("The lines go on a new engrave layer of their own. Engrave them with clean fill settings: they're filled shapes, so the fill's line density sets how solid they come out."),
            },
        };
        var body = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(side, Dock.Right);
        body.Children.Add(side);
        body.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(8), Margin = new(0, 0, 16, 0), Child = _preview });
        Content = body;
        Rows();
        Make();
    }

    EngravingOptions Options() => new()
    {
        Style = (EngravingStyle)Math.Max(0, _style.SelectedIndex),
        SpacingMm = (double)(_spacing.Value ?? 0.6m),
        MaxWidth = (double)(_width.Value ?? 85) / 100,
        AngleDeg = (double)(_angle.Value ?? 30),
        Bend = (double)(_bend.Value ?? 1m),
        Centre = new Vec2(_cx.Value, _cy.Value),
    };

    void Rows()
    {
        var st = (EngravingStyle)Math.Max(0, _style.SelectedIndex);
        _angleRow.IsVisible = st is EngravingStyle.Straight or EngravingStyle.Contour;
        _bendRow.IsVisible = st == EngravingStyle.Contour;
        _centreRow.IsVisible = st is EngravingStyle.Rings or EngravingStyle.Spiral;
    }

    void Later() { _later.Stop(); _later.Start(); }

    async void Make()
    {
        var o = Options();
        _summary.Text = "Drawing the lines…";
        var lines = await Task.Run(() => EngravingLines.Make(_picture, o));
        _lines = lines;
        _preview.Show(lines, _picture.Bounds);
        int ribbons = lines.Subpaths.Length;
        _summary.Text = $"{ribbons:N0} line piece{(ribbons == 1 ? "" : "s")}, {o.SpacingMm:0.##} mm apart, up to {o.SpacingMm * o.MaxWidth:0.##} mm wide, over {_picture.Bounds.Width:0.#} × {_picture.Bounds.Height:0.#} mm.";
    }

    void Add()
    {
        if (_lines.IsEmpty) return;
        var s = _doc.State;
        var layer = Layer.Default(s.Layers.Count, LayerMode.Fill) with { Name = $"Engraving · {(string.IsNullOrWhiteSpace(_picture.Name) ? "picture" : _picture.Name)}" };
        s = Ops.AddLayer(s, layer);
        var shape = new PathShape { Name = "Engraving lines", LayerId = layer.Id, Geometry = _lines };
        s = Ops.AddShapes(s, [shape]);
        // The picture's own layer off, when it holds only this picture (else it would be engraved too).
        if (_hidePicture.IsChecked == true && s.FindLayer(_picture.LayerId) is { } pl && s.ShapesFor(pl).All(x => x.Id == _picture.Id))
            s = Ops.UpdateLayers(s, new HashSet<Guid> { pl.Id }, l => l with { Output = false });
        _doc.Commit("Engraving lines", s, [shape.Id]);
        Close();
    }

    /// <summary>The lines drawn dark on a light ground, fitted to the box.</summary>
    sealed class Preview : Control
    {
        Geometry? _geo;
        Rect2 _area;

        public void Show(PathGeometry g, Rect2 area)
        {
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                ctx.SetFillRule(FillRule.NonZero);
                foreach (var f in g.Flatten(0.02))
                {
                    if (f.Points.Count < 3) continue;
                    ctx.BeginFigure(new Point(f.Points[0].X, f.Points[0].Y), true);
                    for (int i = 1; i < f.Points.Count; i++) ctx.LineTo(new Point(f.Points[i].X, f.Points[i].Y));
                    ctx.EndFigure(true);
                }
            }
            (_geo, _area) = (sg, area);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#E9D7B4")), new Rect(Bounds.Size));
            if (_geo is null || _area.IsEmpty) return;
            double k = Math.Min((Bounds.Width - 20) / _area.Width, (Bounds.Height - 20) / _area.Height);
            var m = Matrix.CreateTranslation(-_area.MinX, -_area.MinY) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation((Bounds.Width - _area.Width * k) / 2, (Bounds.Height - _area.Height * k) / 2);
            using (ctx.PushTransform(m))
                ctx.DrawGeometry(new SolidColorBrush(Color.Parse("#3B2616")), null, _geo);
        }
    }
}
