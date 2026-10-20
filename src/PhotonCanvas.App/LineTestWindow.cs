using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Which settings hide the rows a picture is burned in: the same grey patch burned with a picture
/// layer's settings, then with one thing changed each (spread or not, one way or both, closer
/// lines at the same heat). The camera shows each patch close up; the one with the least visible
/// rows can be put on the picture layer.
/// </summary>
public sealed class LineTestWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly Func<CameraPhoto?> _photo;
    readonly Func<Task> _updatePhoto;

    // Making the test
    readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<Layer> _sources = [];
    readonly NumericUpDown _size = Number(6, 25, 1, "0");
    readonly StackPanel _variants = new() { Spacing = 4 };
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly Button _add = new() { Content = "Add the test to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    // Reading the results
    readonly TextBlock _photoNote = new() { TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel _cards = new();
    readonly ComboBox _useOn = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<Layer> _useLayers = [];
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public LineTestWindow(Document doc, MachineProfile machine, Func<CameraPhoto?> photo, Func<Task> updatePhoto, bool showResults)
    {
        _doc = doc;
        _machine = machine;
        _photo = photo;
        _updatePhoto = updatePhoto;
        Title = "Line test";
        Width = 860;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _size.Value = 10;
        _size.ValueChanged += (_, _) => Plan();
        _source.SelectionChanged += (_, _) => Plan();
        _add.Click += (_, _) => AddTest();

        var make = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Pictures are burned in rows, and sometimes the rows show. This burns the same grey patch (dark, mid and light) with a picture layer's settings, " +
                           "and again with one thing changed each time, so you can see which change hides them.",
                },
                Field("Settings of", _source, tip: "The picture layer to try: every patch starts from exactly its settings."),
                Explained("The patches, left to right", "Each one changes one thing from the first. Closer lines get less power, so they put in the same heat and only the rows differ.", _variants),
                Explained("Patch size", "Bigger patches are easier to judge by eye.", Row(Labelled("Size (mm)", _size))),
                _plan,
                Hint("Use the material you'll engrave the pictures on, flat and in the camera's view, clear of knots and grain lines."),
                _add,
                Hint("Adding the test closes this window so you can move it, frame it and send it. When the job finishes, this window opens again on Compare (or open it with Laser → Line test)."),
            },
        };

        var update = Button("Update photo", "IcCamera", "A fresh camera photo of the burned patches.");
        update.Click += async (_, _) => { update.IsEnabled = false; try { await _updatePhoto(); } finally { update.IsEnabled = true; } ReadPhoto(); };
        var remove = Button("Remove the test from the design", "IcTrash", "Delete the patches and their layers.");
        remove.Click += (_, _) =>
        {
            _doc.Commit("Remove line test", LineTest.Remove(_doc.State));
            ReadPhoto();
            Lists();
        };
        var read = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                Hint("Look at the burned piece itself, close up and in light from the side: single rows are finer than the camera can see. The photos below are to keep track of which patch is which."),
                Row(update),
                _photoNote,
                _cards,
                Explained("Use it", "\"Use these settings\" puts the patch's resolution, power, spreading and direction on this layer. Everything else stays.", Field("Layer", _useOn), _status),
                Row(remove),
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Make the test", Content = new ScrollViewer { Content = make } }, new TabItem { Header = "Compare", Content = new ScrollViewer { Content = read } } } };
        tabs.SelectedIndex = showResults ? 1 : 0;
        Content = new Avalonia.Controls.Border { Padding = new(16), Child = tabs };
        Sources();
        Plan();
        Lists();
        ReadPhoto();
    }

    /// <summary>Is there a line test in the design to compare?</summary>
    public static bool InDesign(DocState s) => LineTest.Patches(s).Count > 0;

    void Sources()
    {
        _sources = [.. _doc.State.Layers.Where(l => l.Mode == LayerMode.Image && l.ImageMode != ImageEngraveMode.Slices && !LineTest.IsTestLayer(l) && !ToneTest.IsTestLayer(l))];
        if (_sources.Count == 0) _sources.Add(Layer.Default(_doc.State.Layers.Count, LayerMode.Image) with { Name = "Pictures" });
        _source.ItemsSource = _sources.Select(l => $"{l.Name} · {Describe(l)}").ToList();
        _source.SelectedIndex = 0;
    }

    static string Describe(Layer l) =>
        $"{l.Laser.Power:0.#}% · {l.Laser.Speed:0} mm/s · {l.Dpi:0} dpi · {(l.ImageMode == ImageEngraveMode.Grayscale ? "greyscale" : l.Dither.ToString().ToLowerInvariant())}";

    Layer SourceLayer() => _sources[Math.Clamp(_source.SelectedIndex, 0, _sources.Count - 1)];

    void Plan()
    {
        var layer = SourceLayer();
        double size = (double)(_size.Value ?? 10);
        var variants = LineTest.Variants(layer);
        _variants.Children.Clear();
        for (int i = 0; i < variants.Count; i++)
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Inlines = [] };
            line.Inlines!.Add(new Avalonia.Controls.Documents.Run($"{i + 1}. {variants[i].Label}") { FontWeight = FontWeight.SemiBold });
            line.Inlines.Add(new Avalonia.Controls.Documents.Run($" — {variants[i].Detail}"));
            _variants.Children.Add(line);
        }
        try
        {
            var (s, _) = LineTest.Add(DocState.New() with { Layers = [] }, layer, size, new Vec2(10, 10));
            var job = JobBuilder.Build(s, new JobOptions());
            _plan.Text = $"{variants.Count} patches · {LineTest.Width(variants.Count, size):0} × {size:0} mm of material · about {Duration(job.EstimateS)} to burn";
            _add.IsEnabled = true;
        }
        catch (Exception e) when (e is JobException or InvalidOperationException or OutsideWorkAreaException)
        {
            _plan.Text = e.Message;
            _add.IsEnabled = false;
        }
    }

    void AddTest()
    {
        var layer = SourceLayer();
        double size = (double)(_size.Value ?? 10);
        double w = LineTest.Width(LineTest.Variants(layer).Count, size);
        var at = new Vec2(_machine.FieldW / 2 - w / 2, _machine.FieldH / 2 - size / 2);
        var (s, ids) = LineTest.Add(LineTest.Remove(_doc.State), layer, size, at);
        _doc.Commit("Line test", s, ids);
        AppLog.Write("line test", $"Test added: {ids.Count} patches of {size:0} mm from {layer.Name}'s settings.");
        Close();
    }

    void Lists()
    {
        _useLayers = [.. _doc.State.Layers.Where(l => l.Mode == LayerMode.Image && !LineTest.IsTestLayer(l) && !ToneTest.IsTestLayer(l))];
        _useOn.ItemsSource = _useLayers.Select(l => l.Name).ToList();
        int match = LineTest.SourceName(_doc.State) is { } name ? _useLayers.FindIndex(l => l.Name == name) : -1;
        _useOn.SelectedIndex = match >= 0 ? match : _useLayers.Count > 0 ? 0 : -1;
    }

    void ReadPhoto()
    {
        _cards.Children.Clear();
        var patches = LineTest.Patches(_doc.State);
        var photo = _photo();
        if (patches.Count == 0) { _photoNote.Text = "There's no line test in this design: make one on the first tab."; return; }
        _photoNote.Text = photo is null ? "No camera photo yet: press Update photo once the patches have been burned." : "";
        for (int i = 0; i < patches.Count; i++)
        {
            var (layer, patch) = patches[i];
            Control face = new Avalonia.Controls.Border { Width = 150, Height = 150, Background = Brush("Bg0"), CornerRadius = new(6) };
            if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, patch.Bounds.Inflate(0.5), 300) is { } jpg)
            {
                try { using var ms = new MemoryStream(jpg); face = new Image { Width = 150, Height = 150, Source = new Avalonia.Media.Imaging.Bitmap(ms), Stretch = Stretch.UniformToFill }; }
                catch (ArgumentException) { }
            }
            var use = new Button { Content = "Use these settings", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            var tested = layer;
            use.Click += (_, _) => Use(tested);
            _cards.Children.Add(new Avalonia.Controls.Border
            {
                Background = Brush("Bg2"), CornerRadius = new(8), Padding = new(8), Margin = new(0, 0, 8, 8), Width = 168,
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        face,
                        new TextBlock { Text = $"{i + 1}. {LineTest.Label(layer)}", FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = $"{layer.Dpi:0} dpi · {layer.Laser.Power:0.#}% · {(layer.Bidirectional ? "both ways" : "one way")} · {(layer.SpreadLines ? "spread" : "in order")}", FontSize = 11.5, Foreground = Brush("TextMuted"), TextWrapping = TextWrapping.Wrap },
                        use,
                    },
                },
            });
        }
    }

    void Use(Layer tested)
    {
        if (_useOn.SelectedIndex < 0 || _useOn.SelectedIndex >= _useLayers.Count) { _status.Text = "There's no picture layer to put them on."; return; }
        var target = _useLayers[_useOn.SelectedIndex];
        var used = LineTest.UseOn(target, tested);
        _doc.Commit($"Line test: {LineTest.Label(tested)}", Ops.UpdateLayers(_doc.State, new HashSet<Guid> { target.Id }, _ => used));
        bool heatChanged = used.Dpi != target.Dpi || used.SpreadLines != target.SpreadLines || used.Bidirectional != target.Bidirectional;
        _status.Text = $"\"{target.Name}\" now burns at {used.Dpi:0} dpi, {used.Laser.Power:0.#}%, {(used.Bidirectional ? "both ways" : "one way")}, {(used.SpreadLines ? "spread out" : "in order")}." +
                       (heatChanged && target.MaterialTone is not null ? " Its tone correction was measured with the old settings: run the tone test again with these." : "");
        _status.Foreground = Brush("Ok");
        AppLog.Write("line test", $"Used \"{LineTest.Label(tested)}\" on {target.Name}.");
    }
}
