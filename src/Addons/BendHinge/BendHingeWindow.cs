using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.BendHinge;

/// <summary>
/// Bend to shape: pick a curve (the selection, or a rounded rectangle or oval typed in), see where
/// the flat panel gets slits and how closely, and put the panel (and formers to wrap it round) in
/// the design.
/// </summary>
public sealed class BendHingeWindow : Window
{
    readonly IAddonHost _host;
    readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "The selected shape", "Rounded rectangle", "Oval" } };
    readonly NumericUpDown _shapeW = Number(10, 2000, 5, "0.#"), _shapeD = Number(10, 2000, 5, "0.#"), _shapeR = Number(0, 1000, 1, "0.#");
    readonly NumericUpDown _height = Number(5, 1000, 5, "0.#"), _thickness = Number(0.5m, 20, 0.5m, "0.##"), _kerf = Number(0, 2, 0.01m, "0.###");
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _cut = Number(2, 500, 1, "0.#"), _gap = Number(0.5m, 50, 0.5m, "0.#"), _minW = Number(0.3m, 20, 0.1m, "0.##"), _maxW = Number(0.5m, 50, 0.5m, "0.#"), _safety = Number(1, 5, 0.1m, "0.0");
    readonly NumericUpDown _calCut = Number(2, 500, 1, "0.#"), _calGap = Number(0.5m, 50, 0.5m, "0.#"), _calRows = Number(0.3m, 20, 0.1m, "0.##"), _calRadius = Number(1, 2000, 1, "0.#");
    readonly CheckBox _formers = new() { Content = "Formers to wrap it round (closed curves)", IsChecked = true };
    readonly CheckBox _marks = new() { Content = "Line-up marks (scored)", IsChecked = true };
    readonly CheckBox _split = new() { Content = "Split into pieces that fit the work area", IsChecked = true };
    readonly StackPanel _shapeSizes = new() { Spacing = 8 };
    readonly StackPanel _calibration = new() { Spacing = 8 };
    readonly BendPreview _preview = new() { Height = 380 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("Danger") };
    readonly TextBlock _notes = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Make the panel", Classes = { "primary" } };
    readonly PathGeometry? _selected;
    HingeMaterial _custom;
    BendHingeResult? _result;
    PathGeometry? _curve;
    bool _loading = true;

    public BendHingeWindow(IAddonHost host)
    {
        _host = host;
        Title = "Bend to shape";
        Width = 940;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var doc = host.Document;
        var picked = doc.State.Shapes.Where(x => doc.Selection.Contains(x.Id)).OfType<PathShape>().ToList();
        if (picked.Count > 0) _selected = new PathGeometry(picked.SelectMany(p => p.World.Subpaths));

        var saved = Saved.Load(host.DataFolder);
        _custom = new HingeMaterial("From your own bend", saved.CustomTwist, saved.CustomBend);
        _material.ItemsSource = HingeMaterial.Presets.Select(m => m.Name).Append("From my own bend…").ToList();
        _material.SelectedIndex = Math.Clamp(saved.Material, 0, HingeMaterial.Presets.Count);
        _source.SelectedIndex = _selected is not null ? 0 : Math.Max(1, saved.Source);
        _shapeW.Value = (decimal)saved.ShapeW; _shapeD.Value = (decimal)saved.ShapeD; _shapeR.Value = (decimal)saved.ShapeR;
        _height.Value = (decimal)saved.Height; _thickness.Value = (decimal)saved.Thickness;
        _kerf.Value = (decimal)(CutLayerKerf() ?? saved.Kerf);
        _cut.Value = (decimal)saved.CutLength; _gap.Value = (decimal)saved.Gap;
        _minW.Value = (decimal)saved.MinSpacing; _maxW.Value = (decimal)saved.MaxSpacing; _safety.Value = (decimal)saved.Safety;
        _calCut.Value = 15; _calGap.Value = 3; _calRows.Value = 1.5m; _calRadius.Value = 20;
        _formers.IsChecked = saved.Formers;
        _marks.IsChecked = saved.Marks;

        _shapeSizes.Children.Add(Row(L("Width", _shapeW), L("Depth", _shapeD)));
        _shapeSizes.Children.Add(L("Corner radius", _shapeR, tip: "Rounded rectangle only. 0 makes sharp corners, which no hinge bends round: the window shows them in red."));
        var useBend = new Button { Content = "Use this bend" };
        useBend.Click += (_, _) => Calibrate();
        _calibration.Children.Add(Hint("A regular living hinge you've bent in this material, and the tightest radius it went round without cracking (in the same thickness and kerf as above):"));
        _calibration.Children.Add(Row(L("Slits", _calCut), L("Gap", _calGap)));
        _calibration.Children.Add(Row(L("Between rows", _calRows), L("Bent round R", _calRadius)));
        _calibration.Children.Add(useBend);

        var help = new Button { Content = "Help" };
        help.Click += (_, _) => _host.ShowHelp("bend-to-shape.md");
        _make.Click += (_, _) => { if (MakeIt()) Close(); };

        var settings = new StackPanel
        {
            Spacing = 8,
            Width = 400,
            Children =
            {
                L("Curve", _source, tip: "The shape the panel bends into, seen from above (the middle of the panel follows it). Draw it first and select it, or type in a rounded rectangle or oval."),
                _shapeSizes,
                Row(L("Panel height", _height, tip: "How tall the bent panel stands: the length of the slits' lines."), L("Thickness", _thickness)),
                Row(L("Material", _material), L("Kerf", _kerf, tip: "How wide the laser cuts. Taken from your Cut layer when it has one.")),
                _calibration,
                Row(L("Slit length", _cut, tip: "Longer slits bend tighter but make a floppier panel."), L("Gap", _gap, tip: "Uncut material between slits in a line.")),
                Row(L("Closest rows", _minW, tip: "The closest lines of slits are allowed to get, for the tightest bends."), L("Widest rows", _maxW, tip: "The furthest apart lines get, for gentle bends. Where even this is more than needed, the sheet is left solid.")),
                L("Safety", _safety, tip: "Make every part able to bend this many times tighter than it needs to. 1.5 is a good start; raise it if a test cracks."),
                _formers,
                _marks,
                _split,
            },
        };
        var right = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Border { Background = Brush("Bg2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Child = _preview },
                _summary,
                _warning,
                _notes,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { help, _make } },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("Auto,16,*") };
        grid.Children.Add(settings);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Content = new Border { Padding = new Thickness(16), Child = grid };

        foreach (var n in new[] { _shapeW, _shapeD, _shapeR, _height, _thickness, _kerf, _cut, _gap, _minW, _maxW, _safety })
            n.ValueChanged += (_, _) => Recompute();
        _source.SelectionChanged += (_, _) => Recompute();
        _material.SelectionChanged += (_, _) => Recompute();
        _formers.IsCheckedChanged += (_, _) => Recompute();
        _split.IsCheckedChanged += (_, _) => Recompute();
        _loading = false;
        Recompute();
    }

    /// <summary>A setting with its name above it, and an explanation on hover.</summary>
    static Control L(string label, Control editor, string? tip = null)
    {
        var c = Labelled(label, editor);
        if (tip is not null) { ToolTip.SetTip(editor, tip); ToolTip.SetTip(c, tip); }
        return c;
    }

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    double? CutLayerKerf()
    {
        var cut = _host.Document.State.Layers.FirstOrDefault(l => l.Mode == LayerMode.Cut && l.ParentId is null && l.KerfMm > 0);
        return cut?.KerfMm;
    }

    HingeMaterial Material() => _material.SelectedIndex >= 0 && _material.SelectedIndex < HingeMaterial.Presets.Count
        ? HingeMaterial.Presets[_material.SelectedIndex]
        : _custom;

    BendHingeSettings Settings() => new()
    {
        Height = V(_height, 60),
        Thickness = V(_thickness, 3),
        Kerf = V(_kerf, 0.15),
        CutLength = V(_cut, 15),
        Gap = V(_gap, 3),
        MinSpacing = V(_minW, 1),
        MaxSpacing = V(_maxW, 6),
        Safety = V(_safety, 1.5),
        Material = Material(),
    };

    PathGeometry? Curve() => _source.SelectedIndex switch
    {
        0 => _selected,
        1 => PathGeometry.RoundRect(0, 0, V(_shapeW, 60), V(_shapeD, 45), V(_shapeR, 12), V(_shapeR, 12)),
        _ => PathGeometry.Ellipse(V(_shapeW, 60) / 2, V(_shapeD, 45) / 2, V(_shapeW, 60) / 2, V(_shapeD, 45) / 2),
    };

    void Calibrate()
    {
        var s = Settings();
        var like = _material.SelectedIndex < HingeMaterial.Presets.Count ? Material() : HingeMaterial.Presets[0];
        var m = BendHinge.Calibrate(like, s.Thickness, s.Kerf, V(_calCut, 15), V(_calGap, 3), V(_calRows, 1.5), V(_calRadius, 20));
        _custom = new HingeMaterial("From your own bend", m.TwistStrain, _custom.BendStrain > 0 ? _custom.BendStrain : like.BendStrain);
        Recompute();
    }

    void Recompute()
    {
        if (_loading) return;
        _shapeSizes.IsVisible = _source.SelectedIndex != 0;
        _calibration.IsVisible = _material.SelectedIndex == HingeMaterial.Presets.Count;
        _warning.Text = "";
        _notes.Text = "";
        _result = null;
        _curve = Curve();
        var s = Settings();
        string? problem = s.Problem();
        if (_curve is null) problem = "Select the curve to bend to first (draw it seen from above), or pick a rounded rectangle or oval.";
        var profile = problem is null ? BendHinge.Profile(_curve!) : null;
        if (problem is null && profile is null) problem = "That shape has no line at least 5 mm long to bend to.";
        if (problem is not null || profile is null)
        {
            _preview.Show(null, problem);
            _summary.Text = "";
            _make.IsEnabled = false;
            return;
        }
        var r = BendHinge.Make(profile, s);
        _result = r;
        double room = Room();
        var splits = r.Length > room && _split.IsChecked == true ? BendHinge.SplitPoints(r, room, out _) : [];
        _preview.Show(r, null, splits);
        int places = r.Bands.Count(b => b.Kind != BandKind.Solid);
        string tightest = double.IsFinite(r.TightestRadius) ? $"Tightest bend R {r.TightestRadius:0.#} mm; " : "";
        _summary.Text = places == 0
            ? $"Panel {r.Length:0.#} × {s.Height:0.#} mm. It bends gently enough to need no slits at all (the sheet takes down to R {BendHinge.SolidRadius(s):0} mm by itself)."
            : $"Panel {r.Length:0.#} × {s.Height:0.#} mm, {r.Lines.Count()} lines of slits in {places} place{(places == 1 ? "" : "s")}. {tightest}these slits reach R {r.ReachableRadius:0.#} mm at the closest rows.";
        var warnings = new List<string>();
        var notes = new List<string>();
        int tight = r.Bands.Count(b => b.Kind == BandKind.TooTight);
        if (tight > 0)
        {
            string fix = r.SuggestedCutLength is { } c
                ? $"make the slits {c:0.#} mm long"
                : "use thinner material or a taller panel with longer slits";
            warnings.Add($"{tight} place{(tight == 1 ? " bends" : "s bend")} tighter than these slits can take (red). Round {(tight == 1 ? "it" : "them")} to at least R {r.ReachableRadius:0.#} mm, or {fix}.");
        }
        _split.IsVisible = r.Length > room;
        if (r.Length > room)
        {
            if (_split.IsChecked == true)
            {
                BendHinge.SplitPoints(r, room, out bool through);
                string joined = profile.Closed && _formers.IsChecked == true ? "butted together on the formers" : "butted together with a strip glued behind each join";
                notes.Add($"The panel is longer than the work area ({room:0} mm), so it's made in {splits.Count + 1} pieces (dashed), {joined}.");
                if (through) warnings.Add("A join had to go through slits: there's no solid stretch long enough there.");
            }
            else warnings.Add($"The panel is longer than the work area ({room:0} mm): cut it in sections, or split it into pieces.");
        }
        if (_formers.IsChecked == true && !profile.Closed)
            notes.Add("The curve is open, so there are no formers: they need a closed shape.");
        _warning.Text = string.Join("\n", warnings);
        _notes.Text = string.Join("\n", notes);
        _warning.IsVisible = warnings.Count > 0;
        _notes.IsVisible = notes.Count > 0;
        _make.IsEnabled = true;
    }

    /// <summary>The longest panel that fits across the work area (less a margin).</summary>
    double Room() => _host.Machine.FieldW - 10;

    bool MakeIt()
    {
        if (_result is not { } r || _curve is null) return false;
        var s = r.Settings;
        var doc = _host.Document;
        var state = doc.State;
        var cut = LayerFor(ref state, LayerMode.Cut, "Cut");
        Layer? score = _marks.IsChecked == true ? LayerFor(ref state, LayerMode.Score, "Score") : null;
        var field = _host.Machine;
        var (panelMarks, formerMarks) = BendHinge.LineUpMarks(r);
        var cuts = r.Length > Room() && _split.IsChecked == true ? BendHinge.SplitPoints(r, Room(), out _) : [];
        var ends = cuts.Prepend(0).Append(r.Length).ToList();
        double x0 = 5, y0 = 10;
        var shapes = new List<Shape>();
        for (int i = 1; i < ends.Count; i++)
        {
            var (outline, slits, marks) = BendHinge.Piece(r, panelMarks, ends[i - 1], ends[i]);
            string piece = ends.Count > 2 ? $" {i} of {ends.Count - 1}" : "";
            var at = Affine.Translate(ends.Count > 2 ? x0 : Math.Max(5, (field.FieldW - r.Length) / 2), y0);
            shapes.Add(new PathShape { Name = $"Bend to shape · panel{piece}", LayerId = cut.Id, Geometry = outline.Transform(at) });
            if (!slits.IsEmpty) shapes.Add(new PathShape { Name = $"Bend to shape · slits{piece}", LayerId = cut.Id, Geometry = slits.Transform(at) });
            if (score is not null && !marks.IsEmpty)
                shapes.Add(new PathShape { Name = $"Bend to shape · line-up marks{piece}", LayerId = score.Id, Geometry = marks.Transform(at) });
            y0 += s.Height + 5;
        }
        if (_formers.IsChecked == true && r.Profile.Closed)
        {
            var former = BendHinge.Former(_curve, s.Thickness);
            var box = former.Bounds();
            if (!former.IsEmpty && !box.IsEmpty)
            {
                double fx = x0, fy = y0 + 3;
                foreach (var which in new[] { "top", "bottom" })
                {
                    var move = Affine.Translate(fx - box.MinX, fy - box.MinY);
                    shapes.Add(new PathShape { Name = $"Bend to shape · former ({which})", LayerId = cut.Id, Geometry = former.Transform(move) });
                    if (score is not null && !formerMarks.IsEmpty)
                        shapes.Add(new PathShape { Name = $"Bend to shape · former marks ({which})", LayerId = score.Id, Geometry = formerMarks.Transform(move) });
                    fx += box.Width + 5;
                }
            }
        }
        var (next, _) = Ops.Group(Ops.AddShapes(state, shapes), shapes.Select(x => x.Id).ToHashSet());
        doc.Commit("Bend to shape", next, shapes.Select(x => x.Id));
        Save();
        _host.Status($"Bend to shape: a {r.Length:0.#} × {s.Height:0.#} mm panel with {r.Lines.Count()} lines of slits.");
        return true;
    }

    static Layer LayerFor(ref DocState s, LayerMode mode, string name)
    {
        var existing = s.Layers.FirstOrDefault(l => l.Mode == mode && l.Visible && !l.Locked && l.ParentId is null);
        if (existing is not null) return existing;
        var layer = Layer.Default(s.Layers.Count, mode) with { Name = name };
        s = Ops.AddLayer(s, layer);
        return layer;
    }

    void Save()
    {
        var s = Settings();
        new Saved
        {
            Source = _source.SelectedIndex,
            ShapeW = V(_shapeW, 60), ShapeD = V(_shapeD, 45), ShapeR = V(_shapeR, 12),
            Height = s.Height, Thickness = s.Thickness, Kerf = s.Kerf, CutLength = s.CutLength, Gap = s.Gap,
            MinSpacing = s.MinSpacing, MaxSpacing = s.MaxSpacing, Safety = s.Safety,
            Material = _material.SelectedIndex, CustomTwist = _custom.TwistStrain, CustomBend = _custom.BendStrain,
            Formers = _formers.IsChecked == true, Marks = _marks.IsChecked == true,
        }.Store(_host.DataFolder);
    }

    /// <summary>The window's settings as last used, kept in the add-on's data folder.</summary>
    sealed class Saved
    {
        public int Source { get; set; } = 1;
        public double ShapeW { get; set; } = 60;
        public double ShapeD { get; set; } = 45;
        public double ShapeR { get; set; } = 12;
        public double Height { get; set; } = 50;
        public double Thickness { get; set; } = 3;
        public double Kerf { get; set; } = 0.15;
        public double CutLength { get; set; } = 15;
        public double Gap { get; set; } = 3;
        public double MinSpacing { get; set; } = 1;
        public double MaxSpacing { get; set; } = 6;
        public double Safety { get; set; } = 1.5;
        public int Material { get; set; }
        public double CustomTwist { get; set; } = HingeMaterial.Presets[0].TwistStrain;
        public double CustomBend { get; set; } = HingeMaterial.Presets[0].BendStrain;
        public bool Formers { get; set; } = true;
        public bool Marks { get; set; } = true;

        const string FileName = "settings.json";
        // Its own resolver: the default JSON options would keep this add-on in memory after it stops.
        static readonly JsonSerializerOptions Json = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

        public static Saved Load(string folder)
        {
            try
            {
                var path = Path.Combine(folder, FileName);
                if (File.Exists(path) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), Json) is { } s) return s;
            }
            catch (Exception) { /* a damaged file just means the defaults */ }
            return new Saved();
        }

        public void Store(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, Json));
            }
            catch (Exception) { /* not being able to remember the settings isn't worth an error */ }
        }
    }
}
