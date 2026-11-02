using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.App;
using static PhotonCanvas.App.UiKit;

// Not ...Addon.Generators: inside that namespace "Generators" would mean it, not the Doc class that draws the projects.
namespace PhotonCanvas.Addon.ReadyMade;

/// <summary>Make a finger-jointed box, a keychain or tag, a living hinge, earrings, or the selection in several sizes.</summary>
public sealed class GeneratorsWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _field;
    readonly NumericUpDown _bw = Number(10, 1000, 5, "0.#"), _bd = Number(10, 1000, 5, "0.#"), _bh = Number(5, 1000, 5, "0.#"), _bt = Number(0.5m, 20, 0.5m, "0.##"), _bf = Number(1, 100, 1, "0.#");
    readonly CheckBox _lid = new() { Content = "With a lid", IsChecked = true };
    // The box's cut settings from the library: the material's thickness, and the kerf measured for them (Kerf gauge or Cut test).
    readonly ComboBox _boxCut = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _boxCutNote = Hint();
    readonly List<(Material M, Recipe R)> _cuts = [];
    readonly ComboBox _tagShape = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Rounded rectangle", "Circle", "Tag" }, SelectedIndex = 0 };
    readonly NumericUpDown _kw = Number(10, 300, 1, "0.#"), _kh = Number(5, 300, 1, "0.#"), _kHole = Number(0, 30, 0.5m, "0.#"), _kInset = Number(0.5m, 50, 0.5m, "0.#");
    readonly TextBox _kText = new() { Watermark = "Text on it (optional; fields like {serial} work)" };
    readonly TextBox _sizes = new() { Text = "40, 60, 80", Watermark = "Widths in mm, separated by commas" };
    readonly NumericUpDown _hw = Number(5, 1000, 5, "0.#"), _hh = Number(5, 1000, 5, "0.#"), _hCut = Number(1, 200, 1, "0.#"), _hGap = Number(0.5m, 50, 0.5m, "0.#"), _hSpace = Number(0.3m, 20, 0.1m, "0.##");
    readonly CheckBox _hOutline = new() { Content = "With the panel's outline", IsChecked = true };
    readonly ComboBox _eShape = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Teardrop", "Leaf", "Circle", "Hexagon", "Arch" }, SelectedIndex = 0 };
    readonly NumericUpDown _ew = Number(5, 100, 1, "0.#"), _eh = Number(5, 150, 1, "0.#"), _eHole = Number(0, 10, 0.1m, "0.#"), _eInset = Number(0.3m, 20, 0.1m, "0.#");

    public GeneratorsWindow(Document doc, MachineProfile field, MaterialLibrary? library = null)
    {
        _doc = doc;
        _field = field;
        // Measured recipes first: those are the ones whose fingers fit.
        _cuts.AddRange((library?.Materials ?? []).SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => (m, r))).OrderByDescending(x => x.r.Settings.KerfMm > 0));
        _boxCut.ItemsSource = _cuts.Select(x => $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : " (no kerf measured)")).Prepend("The design's cut layer, as it is").ToList();
        _boxCut.SelectedIndex = _cuts.Count > 0 && _cuts[0].R.Settings.KerfMm > 0 ? 1 : 0;
        _boxCut.SelectionChanged += (_, _) => BoxCut();
        Title = "Generators";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _bw.Value = 80; _bd.Value = 60; _bh.Value = 40; _bt.Value = 3; _bf.Value = 9;
        _kw.Value = 50; _kh.Value = 25; _kHole.Value = 4; _kInset.Value = 3;
        _hw.Value = 60; _hh.Value = 40; _hCut.Value = 15; _hGap.Value = 3; _hSpace.Value = 1.5m;
        _ew.Value = 25; _eh.Value = 40; _eHole.Value = 1.5m; _eInset.Value = 1.5m;
        var makeHinge = new Button { Content = "Make the hinge", Classes = { "primary" } };
        makeHinge.Click += (_, _) => { MakeHinge(); Close(); };
        var makeEarrings = new Button { Content = "Make a pair", Classes = { "primary" } };
        makeEarrings.Click += (_, _) => { MakeEarrings(); Close(); };
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
                            Field("Cut with", _boxCut, tip: "Cut settings from your library. The panels go on a cut layer with them, kerf included, so the fingers come out the width they're drawn and press together snugly."),
                            _boxCutNote,
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
                    Header = "Living hinge",
                    Content = new StackPanel
                    {
                        Spacing = 8, Margin = new(0, 10, 0, 0),
                        Children =
                        {
                            Row(Field("Width", _hw), Field("Height", _hh)),
                            Row(Field("Cut length", _hCut), Field("Gap", _hGap)),
                            Field("Between rows", _hSpace, tip: "Closer rows bend more easily but the strips get thinner. In 3 mm plywood, 1.5 mm is a good start."),
                            _hOutline,
                            Hint("Cuts run up and down, so the panel bends around an up-and-down line. They go on a Cut layer."),
                            makeHinge,
                        },
                    },
                },
                new TabItem
                {
                    Header = "Earrings",
                    Content = new StackPanel
                    {
                        Spacing = 8, Margin = new(0, 10, 0, 0),
                        Children =
                        {
                            Field("Shape", _eShape),
                            Row(Field("Width", _ew), Field("Height", _eh)),
                            Row(Field("Hook hole Ø", _eHole), Field("from the top", _eInset)),
                            Hint("A matching pair, on a Cut layer."),
                            makeEarrings,
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
        BoxCut();
    }

    (Material M, Recipe R)? BoxRecipe => _boxCut.SelectedIndex > 0 && _boxCut.SelectedIndex <= _cuts.Count ? _cuts[_boxCut.SelectedIndex - 1] : null;

    void BoxCut()
    {
        if (BoxRecipe is not { } c)
        {
            _boxCutNote.Text = "The panels go on the design's Cut layer. Set its kerf (More settings) so the fingers fit snugly.";
            return;
        }
        if (c.M.ThicknessMm is { } t) _bt.Value = (decimal)Math.Clamp(t, 0.5, 20);
        _boxCutNote.Text = c.R.Settings.KerfMm > 0
            ? $"On a cut layer \"{c.M.Label}\" with a {c.R.Settings.KerfMm:0.###} mm kerf: the fingers press together snugly."
            : "These settings have no kerf measured yet, so the fingers will be loose: measure it first (Laser → Kerf gauge, or Cut test).";
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
        Layer layer;
        if (BoxRecipe is { } c)
        {
            // The recipe's own cut layer: one made before for it, else a new one.
            var made = s.Layers.FirstOrDefault(l => l.Mode == LayerMode.Cut && l.ParentId is null && l.Material?.RecipeId == c.R.Id && Math.Abs(l.KerfMm - c.R.Settings.KerfMm) < 1e-9);
            if (made is null)
            {
                made = c.R.Settings.ApplyTo(Layer.Default(s.Layers.Count, LayerMode.Cut)) with { Name = c.M.Label, Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) };
                s = Ops.AddLayer(s, made);
            }
            layer = made;
        }
        else layer = LayerFor(ref s, LayerMode.Cut, "Cut");
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

    void MakeHinge()
    {
        var s = _doc.State;
        var layer = LayerFor(ref s, LayerMode.Cut, "Cut");
        double w = (double)(_hw.Value ?? 60), h = (double)(_hh.Value ?? 40);
        var at = Affine.Translate(_field.FieldW / 2 - w / 2, _field.FieldH / 2 - h / 2);
        var shapes = new List<Shape>
        {
            new PathShape { Name = "Living hinge", LayerId = layer.Id, Geometry = Generators.LivingHinge(w, h, (double)(_hCut.Value ?? 15), (double)(_hGap.Value ?? 3), (double)(_hSpace.Value ?? 1.5m)).Transform(at) },
        };
        if (_hOutline.IsChecked == true) shapes.Add(new PathShape { Name = "Hinge panel", LayerId = layer.Id, Geometry = PathGeometry.Rect(0, 0, w, h).Transform(at) });
        var (ns, _) = Ops.Group(Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToHashSet());
        _doc.Commit("Living hinge", ns, shapes.Select(x => x.Id));
    }

    void MakeEarrings()
    {
        var s = _doc.State;
        var layer = LayerFor(ref s, LayerMode.Cut, "Cut");
        double w = (double)(_ew.Value ?? 25), h = (double)(_eh.Value ?? 40);
        var g = Generators.Earring((Generators.EarringShape)Math.Max(0, _eShape.SelectedIndex), w, h, (double)(_eHole.Value ?? 1.5m), (double)(_eInset.Value ?? 1.5m));
        double x = _field.FieldW / 2 - w - 2.5, y = _field.FieldH / 2 - h / 2;
        var shapes = new List<Shape>
        {
            new PathShape { Name = "Earring (left)", LayerId = layer.Id, Geometry = g.Transform(Affine.Translate(x, y)) },
            // The right one mirrored, so a lopsided shape makes a matching pair.
            new PathShape { Name = "Earring (right)", LayerId = layer.Id, Geometry = g.Transform(Affine.Scale(-1, 1).Then(Affine.Translate(x + 2 * w + 5, y))) },
        };
        var (ns, _) = Ops.Group(Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToHashSet());
        _doc.Commit("Earrings", ns, shapes.Select(x => x.Id));
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
