using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// Split with joints: the selected outlines cut into pieces that each fit the bed, joined back
/// together edge to edge with dovetails, jigsaw knobs or fingers, and spread a little apart.
/// </summary>
public sealed class SplitWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly List<PathShape> _shapes;
    readonly ComboBox _direction = Combo("Across the longer side", "Up and down (cuts across the width)", "Side to side (cuts across the height)");
    readonly ComboBox _kind = Combo("Dovetails", "Jigsaw knobs", "Square fingers", "Straight");
    readonly NumericUpDown _pieces = Number(0, 20, 1, "0"), _size = Number(2, 50, 0.5m, "0.#"), _fit = Number(-0.5m, 1, 0.05m, "0.##"), _margin = Number(0, 50, 0.5m, "0.#"), _spread = Number(0, 100, 1, "0.#");
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly Preview _preview = new();
    readonly Button _split = new() { Content = "Split", Classes = { "primary" }, MinWidth = 120 };
    List<(PathShape From, List<PathsD> Pieces)> _result = [];
    bool _alongX;

    public SplitWindow(Document doc, MachineProfile machine, IReadOnlyList<PathShape> shapes)
    {
        _doc = doc;
        _machine = machine;
        _shapes = [.. shapes];
        Title = "Split with joints";
        Width = 860;
        Height = 560;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _pieces.Value = 0;
        _size.Value = 8;
        _fit.Value = 0;
        _margin.Value = 3;
        _spread.Value = 5;
        var left = new StackPanel
        {
            Spacing = 10, Width = 320,
            Children =
            {
                Hint("Cuts the selected outline into pieces that each fit the bed, with joins that fit back together edge to edge. Cut them, then glue them up."),
                Field("Cut", _direction),
                Field("Pieces", _pieces, tip: "0: as few as fit the bed."),
                Field("Join", _kind, tip: "Dovetails and jigsaw knobs lock the pieces together in the plane; square fingers and a straight cut need glue (and a strip behind)."),
                Field("Size", _size, tip: "How far each tail reaches across the seam (mm). About 2½ times the sheet thickness is a good start."),
                Field("Fit", _fit, tip: "Play between the pieces on top of the kerf: positive looser, negative tighter."),
                Field("From the edges", _margin, tip: "How far from the outline's edges the tails stop, so nothing thin breaks off there."),
                Field("Spread apart", _spread, tip: "How far apart the pieces are put, so you can see them (mm)."),
                _summary,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        buttons.Children.Add(_split);
        buttons.Children.Add(close);
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto"), Margin = new Thickness(16), ColumnSpacing = 16 };
        grid.Children.Add(left);
        var frame = new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _preview };
        Grid.SetColumn(frame, 1);
        grid.Children.Add(frame);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
        foreach (var n in new[] { _pieces, _size, _fit, _margin, _spread }) n.ValueChanged += (_, _) => Update();
        foreach (var c in new[] { _direction, _kind }) c.SelectionChanged += (_, _) => Update();
        _split.Click += (_, _) => { Apply(); Close(); };
        Update();
    }

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = 0 };

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    JoinStyle Style => new((JoinKind)Math.Max(0, _kind.SelectedIndex), V(_size, 8), V(_fit, 0));

    /// <summary>The pieces each outline is cut into, with these settings (flat, y up).</summary>
    public static List<PathsD> Cut(PathsD shape, bool alongX, int pieces, JoinStyle style, double margin, double maxLength)
    {
        var b = Geo.Bounds(shape);
        double lo = alongX ? b.left : b.top, hi = alongX ? b.right : b.bottom;
        List<double> seams = pieces > 1
            ? [.. Enumerable.Range(1, pieces - 1).Select(i => lo + (hi - lo) * i / pieces)]
            : pieces == 1 ? [] : Joins.Seams(lo, hi, maxLength, style);
        return seams.Count == 0 ? [shape] : Joins.Split(shape, alongX, seams, style, margin);
    }

    void Update()
    {
        var all = _shapes.Select(s => Geo.FromGeometry(s.World).Areas).ToList();
        var union = Geo.Tidy(new PathsD(all.SelectMany(a => a)));
        var b = Geo.Bounds(union);
        _alongX = _direction.SelectedIndex switch { 1 => true, 2 => false, _ => b.right - b.left >= b.bottom - b.top };
        double maxLen = (_alongX ? _machine.FieldW : _machine.FieldH) - 2 * BoxMaker.BedGap;
        var style = Style;
        _result = [.. _shapes.Select((s, i) => (s, Cut(all[i], _alongX, (int)V(_pieces, 0), style, V(_margin, 3), maxLen)))];
        int most = _result.Count == 0 ? 0 : _result.Max(r => r.Pieces.Count);
        double longest = _result.SelectMany(r => r.Pieces).Select(p => { var q = Geo.Bounds(p); return _alongX ? q.right - q.left : q.bottom - q.top; }).DefaultIfEmpty(0).Max();
        _summary.Text = most <= 1
            ? "It fits the bed as it is: nothing to split. Set Pieces to cut it anyway."
            : $"{most} pieces, the longest {longest:0} mm along the cut (the bed is {(_alongX ? _machine.FieldW : _machine.FieldH):0} mm).";
        _split.IsEnabled = most > 1;
        _preview.Show([.. _result.SelectMany(r => r.Pieces)], _alongX, V(_spread, 5));
    }

    void Apply()
    {
        var s = _doc.State;
        var made = new List<Shape>();
        double spread = V(_spread, 5);
        foreach (var (from, pieces) in _result)
        {
            if (pieces.Count < 2) continue;
            for (int i = 0; i < pieces.Count; i++)
            {
                var g = Geo.ToGeometry(pieces[i]).Transform(Affine.Translate(_alongX ? i * spread : 0, _alongX ? 0 : i * spread));
                made.Add(new PathShape { Name = $"{from.Name} · piece {i + 1} of {pieces.Count}", LayerId = from.LayerId, Geometry = g });
            }
            s = Ops.Delete(s, new HashSet<Guid> { from.Id });
        }
        if (made.Count == 0) return;
        _doc.Commit("Split with joints", Ops.AddShapes(s, made), made.Select(m => m.Id));
    }

    /// <summary>The pieces, spread apart, with their joins.</summary>
    sealed class Preview : Control
    {
        List<PathsD> _pieces = [];
        bool _alongX;
        double _spread;

        public void Show(List<PathsD> pieces, bool alongX, double spread)
        {
            _pieces = pieces;
            _alongX = alongX;
            _spread = spread;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brush("Bg2"), new Rect(Bounds.Size));
            if (_pieces.Count == 0) return;
            // In the design's own coordinates (y down), spread as they will be.
            var moved = _pieces.Select((p, i) => Geo.Move(Geo.Map(p, q => new PointD(q.x, -q.y)), _alongX ? i * _spread : 0, _alongX ? 0 : i * _spread)).ToList();
            var b = Geo.Bounds(new PathsD(moved.SelectMany(m => m)));
            double w = b.right - b.left, h = b.bottom - b.top;
            if (w <= 0 || h <= 0) return;
            double k = Math.Min((Bounds.Width - 30) / w, (Bounds.Height - 30) / h);
            double ox = (Bounds.Width - w * k) / 2 - b.left * k, oy = (Bounds.Height - h * k) / 2 - b.top * k;
            var fill = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E)), 1);
            foreach (var p in moved)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
                    foreach (var path in p)
                    {
                        if (path.Count < 3) continue;
                        c.BeginFigure(new Point(ox + path[0].x * k, oy + path[0].y * k), true);
                        foreach (var q in path.Skip(1)) c.LineTo(new Point(ox + q.x * k, oy + q.y * k));
                        c.EndFigure(true);
                    }
                }
                ctx.DrawGeometry(fill, pen, g);
            }
        }
    }
}
