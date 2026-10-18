using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Make a finger-jointed box, a keychain or tag, or the selection in several sizes.</summary>
public sealed class GeneratorsWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _field;
    readonly NumericUpDown _bw = Number(10, 1000, 5, "0.#"), _bd = Number(10, 1000, 5, "0.#"), _bh = Number(5, 1000, 5, "0.#"), _bt = Number(0.5m, 20, 0.5m, "0.##"), _bf = Number(1, 100, 1, "0.#");
    readonly CheckBox _lid = new() { Content = "With a lid", IsChecked = true };
    readonly ComboBox _tagShape = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Rounded rectangle", "Circle", "Tag" }, SelectedIndex = 0 };
    readonly NumericUpDown _kw = Number(10, 300, 1, "0.#"), _kh = Number(5, 300, 1, "0.#"), _kHole = Number(0, 30, 0.5m, "0.#"), _kInset = Number(0.5m, 50, 0.5m, "0.#");
    readonly TextBox _kText = new() { Watermark = "Text on it (optional; fields like {serial} work)" };
    readonly TextBox _sizes = new() { Text = "40, 60, 80", Watermark = "Widths in mm, separated by commas" };

    public GeneratorsWindow(Document doc, MachineProfile field)
    {
        _doc = doc;
        _field = field;
        Title = "Generators";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _bw.Value = 80; _bd.Value = 60; _bh.Value = 40; _bt.Value = 3; _bf.Value = 9;
        _kw.Value = 50; _kh.Value = 25; _kHole.Value = 4; _kInset.Value = 3;
        var makeBox = new Button { Content = "Make the box", Classes = { "primary" } };
        makeBox.Click += (_, _) => { MakeBox(); Close(); };
        var makeTag = new Button { Content = "Make it", Classes = { "primary" } };
        makeTag.Click += (_, _) => { MakeTag(); Close(); };
        var makeSizes = new Button { Content = "Make the sizes", Classes = { "primary" }, IsEnabled = _doc.Selection.Count > 0 };
        makeSizes.Click += (_, _) => { MakeSizes(); Close(); };
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem
                {
                    Header = "Box",
                    Content = new StackPanel
                    {
                        Spacing = 8, Margin = new(0, 10, 0, 0),
                        Children =
                        {
                            Row(Field("Inside width", _bw), Field("depth", _bd)),
                            Row(Field("Inside height", _bh), Field("Material", _bt)),
                            Field("Finger width", _bf, tip: "About three times the material thickness is a good start. Each edge gets an odd number of fingers so the corners work out."),
                            _lid,
                            Hint("The panels go on a Cut layer. Set its kerf (More settings) so the fingers fit snugly."),
                            makeBox,
                        },
                    },
                },
                new TabItem
                {
                    Header = "Keychain / tag",
                    Content = new StackPanel
                    {
                        Spacing = 8, Margin = new(0, 10, 0, 0),
                        Children =
                        {
                            Field("Shape", _tagShape),
                            Row(Field("Width", _kw), Field("Height", _kh)),
                            Row(Field("Hole Ø", _kHole), Field("from the edge", _kInset)),
                            _kText,
                            Hint("The outline goes on a Cut layer, the text on an Engrave layer, fitted inside."),
                            makeTag,
                        },
                    },
                },
                new TabItem
                {
                    Header = "Sizes",
                    Content = new StackPanel
                    {
                        Spacing = 8, Margin = new(0, 10, 0, 0),
                        Children =
                        {
                            Hint("The selection in several sizes, side by side (each keeps its proportions)."),
                            Field("Widths (mm)", _sizes),
                            makeSizes,
                        },
                    },
                },
            },
        };
        Content = new Border { Padding = new Thickness(16), Child = tabs };
    }

    Layer LayerFor(ref DocState s, LayerMode mode, string name)
    {
        var existing = s.Layers.FirstOrDefault(l => l.Mode == mode && l.Visible && !l.Locked && l.ParentId is null);
        if (existing is not null) return existing;
        var layer = Layer.Default(s.Layers.Count, mode) with { Name = name };
        s = Ops.AddLayer(s, layer);
        return layer;
    }

    void MakeBox()
    {
        var s = _doc.State;
        var layer = LayerFor(ref s, LayerMode.Cut, "Cut");
        var panels = Generators.Box((double)(_bw.Value ?? 80), (double)(_bd.Value ?? 60), (double)(_bh.Value ?? 40), (double)(_bt.Value ?? 3), (double)(_bf.Value ?? 9), _lid.IsChecked == true);
        var shapes = panels.Select(p => (Shape)new PathShape { Name = $"Box · {p.Name}", LayerId = layer.Id, Geometry = p.Outline.Transform(Affine.Translate(10, 10)) }).ToList();
        var (ns, gid) = Ops.Group(Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToHashSet());
        _doc.Commit("Box", ns, shapes.Select(x => x.Id));
    }

    void MakeTag()
    {
        var s = _doc.State;
        var cut = LayerFor(ref s, LayerMode.Cut, "Cut");
        double w = (double)(_kw.Value ?? 50), h = (double)(_kh.Value ?? 25), hole = (double)(_kHole.Value ?? 4), inset = (double)(_kInset.Value ?? 3);
        var kind = (Generators.TagShape)Math.Max(0, _tagShape.SelectedIndex);
        var at = new Vec2(_field.FieldW / 2 - w / 2, _field.FieldH / 2 - h / 2);
        var outline = new PathShape { Name = "Keychain", LayerId = cut.Id, Geometry = Generators.Keychain(kind, w, h, hole, inset).Transform(Affine.Translate(at)) };
        var shapes = new List<Shape> { outline };
        if (!string.IsNullOrWhiteSpace(_kText.Text))
        {
            var engrave = LayerFor(ref s, LayerMode.Fill, "Engrave");
            // The text fitted into the part beside the hole, with a margin.
            double left = kind == Generators.TagShape.Circle ? w * 0.15 : inset + hole + 2, top = kind == Generators.TagShape.Circle ? inset + hole + 2 : h * 0.2;
            double boxW = w - left - w * 0.1, boxH = h - top - h * 0.2;
            var text = TextShape.Create(_kText.Text, new TextStyle("Arial", Math.Max(1, boxH * 0.7), FitWidthMm: Math.Max(1, boxW), FitHeightMm: Math.Max(1, boxH)), engrave.Id, Affine.Translate(at.X + left, at.Y + top));
            shapes.Add(text);
        }
        var (ns, _) = Ops.Group(Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToHashSet());
        _doc.Commit("Keychain", ns, shapes.Select(x => x.Id));
    }

    void MakeSizes()
    {
        var ids = _doc.Selection;
        var box = _doc.State.Bounds(ids);
        if (box.IsEmpty) return;
        var widths = (_sizes.Text ?? "").Split(',', ';', ' ').Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0).Where(v => v > 0).ToList();
        var originals = _doc.State.Shapes.Where(x => ids.Contains(x.Id)).ToList();
        var made = new List<Shape>();
        double x0 = box.MaxX + 5;
        foreach (var w in widths)
        {
            double k = w / box.Width;
            var t = Affine.Translate(-box.MinX, -box.MinY).Then(Affine.Scale(k, k)).Then(Affine.Translate(x0, box.MinY));
            made.AddRange(Ops.CopyAsUnit(originals, t));
            x0 += w + 5;
        }
        if (made.Count > 0) _doc.Commit("Sizes", Ops.AddShapes(_doc.State, made), made.Select(m => m.Id));
    }
}
