using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Photos that come out as they look: a row of grey patches burned with a picture layer's own
/// settings, the camera reads how light each came out, and that layer's pictures are corrected so
/// their greys land where they should. Shows the picture as it would burn, plain and corrected,
/// in the material's own colours.
/// </summary>
public sealed class ToneTestWindow : Window
{
    readonly Document _doc;
    readonly MaterialLibrary _lib;
    readonly MachineProfile _machine;
    readonly Func<CameraPhoto?> _photo;
    readonly Func<Task> _updatePhoto;

    // Making the test
    readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<Layer?> _sources = [];
    readonly NumericUpDown _steps = Number(5, 41, 1, "0"), _size = Number(3, 15, 1, "0.#");
    readonly WedgePreview _wedge = new() { Margin = new(4) };
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly Button _add = new() { Content = "Add the test to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    // Reading the results
    readonly TextBlock _photoNote = new() { TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel _patches = new();
    readonly ToneChart _chart = new() { Height = 220, Margin = new(4) };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly ComboBox _picture = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<ImageShape> _pictures = [];
    readonly Image _original = new() { Stretch = Stretch.Uniform, Height = 170 }, _plainBurn = new() { Stretch = Stretch.Uniform, Height = 170 }, _fixedBurn = new() { Stretch = Stretch.Uniform, Height = 170 };
    readonly ComboBox _useOn = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<Layer> _useLayers = [];
    readonly Button _apply = new() { Content = "Correct this layer's pictures", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = false };
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Basswood" };
    readonly Button _save = new() { Content = "Save to the library", Classes = { "primary" }, IsEnabled = false };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    ToneReading? _reading;

    public ToneTestWindow(Document doc, MaterialLibrary lib, MachineProfile machine, Func<CameraPhoto?> photo, Func<Task> updatePhoto, bool showResults)
    {
        _doc = doc;
        _lib = lib;
        _machine = machine;
        _photo = photo;
        _updatePhoto = updatePhoto;
        Title = "Tone test";
        Width = 820;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _steps.Value = 11;
        _size.Value = 6;
        _steps.ValueChanged += (_, _) => Plan();
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
                    Text = "This burns a row of grey patches, black to white, with a picture layer's own settings. The camera then reads how dark each one really came out " +
                           "on this material, and that layer's pictures are corrected so their greys come out as they look on screen.",
                },
                Field("Settings of", _source, tip: "The picture layer to match: the test burns with exactly its settings (power, speed, resolution, dithering)."),
                new Border { Background = Brush("Bg0"), CornerRadius = new(8), Padding = new(10), Child = _wedge },
                Explained("Patches", "More patches read the curve more finely; bigger ones are easier for the camera to read.",
                    Row(Labelled("How many", _steps), Labelled("Size (mm)", _size))),
                _plan,
                Hint("Use the material you'll engrave the pictures on, flat and in the camera's view. Keep the patches clear of knots and grain lines."),
                _add,
                Hint("Adding the test closes this window so you can move it, frame it and send it. When the job finishes, this window opens again on Read the results (or open it with Laser → Tone test)."),
            },
        };

        var update = Button("Update photo", "IcCamera", "A fresh camera photo of the burned test.");
        update.Click += async (_, _) => { update.IsEnabled = false; try { await _updatePhoto(); } finally { update.IsEnabled = true; } ReadPhoto(); };
        _picture.SelectionChanged += (_, _) => Preview();
        _apply.Click += (_, _) => Apply();
        _material.SelectionChanged += (_, _) => _newName.IsVisible = _material.SelectedIndex == 0;
        _save.Click += (_, _) => Save();
        var previews = new Grid { ColumnDefinitions = new("*,8,*,8,*") };
        void Put(Control c, int col) { Grid.SetColumn(c, col); previews.Children.Add(c); }
        Put(Labelled("Your picture", _original), 0);
        Put(Labelled("Burned as it is", _plainBurn), 2);
        Put(Labelled("Burned corrected", _fixedBurn), 4);
        var read = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                Hint("Take a photo of the burned strip (the camera must see all of it); the patches are found wherever you moved it to."),
                Row(update),
                _photoNote,
                new ScrollViewer { Content = _patches, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
                Explained("How this material takes greys", "Dots: each patch as read. The straight dashed line is what you'd want; the further the dots are from it, the more pictures change when corrected.", _chart),
                _summary,
                Explained("See it before you burn", "One of your pictures in this material's own colours: as it would burn now, and corrected.",
                    Field("Picture", _picture), previews),
                Explained("Use it", "Set the correction on a picture layer (it goes with the layer's settings), and keep it in the material library.",
                    Field("Layer", _useOn), _apply, Field("Material", _material), _newName, _save, _status),
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Make the test", Content = new ScrollViewer { Content = make } }, new TabItem { Header = "Read the results", Content = new ScrollViewer { Content = read } } } };
        tabs.SelectedIndex = showResults ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        Sources();
        Plan();
        Lists();
        ReadPhoto();
    }

    /// <summary>Is there a tone test in the design to read?</summary>
    public static bool InDesign(DocState s) => ToneTest.Strip(s) is not null;

    void Sources()
    {
        _sources = [.. _doc.State.Layers.Where(l => l.Mode == LayerMode.Image && !ToneTest.IsTestLayer(l)).Cast<Layer?>().Append(null)];
        _source.ItemsSource = _sources.Select(l => l is null ? "Picture settings for a new layer" : $"{l.Name} · {Describe(l)}").ToList();
        _source.SelectedIndex = 0;
    }

    static string Describe(Layer l) =>
        $"{l.Laser.Power:0.#}% · {l.Laser.Speed:0} mm/s · {l.Dpi:0} dpi · {(l.ImageMode == ImageEngraveMode.Grayscale ? "greyscale" : l.Dither.ToString().ToLowerInvariant())}";

    Layer SourceLayer() =>
        _sources.ElementAtOrDefault(Math.Max(0, _source.SelectedIndex)) ?? Layer.Default(_doc.State.Layers.Count, LayerMode.Image) with { Name = "Pictures" };

    void Plan()
    {
        int steps = (int)(_steps.Value ?? 11);
        double size = (double)(_size.Value ?? 6);
        _wedge.Show(ToneTest.Levels(steps));
        try
        {
            var (s, _) = ToneTest.Add(DocState.New() with { Layers = [] }, SourceLayer(), steps, size, new Vec2(10, 10));
            var job = JobBuilder.Build(s, new JobOptions());
            _plan.Text = $"{steps} patches · {ToneTest.Width(steps, size):0} × {size:0.#} mm of material · about {Duration(job.EstimateS)} to burn with {SourceLayer().Name}'s settings";
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
        int steps = (int)(_steps.Value ?? 11);
        double size = (double)(_size.Value ?? 6);
        double w = ToneTest.Width(steps, size);
        var at = new Vec2(_machine.FieldW / 2 - w / 2, _machine.FieldH / 2 - size / 2);
        var (s, id) = ToneTest.Add(_doc.State, SourceLayer(), steps, size, at);
        _doc.Commit("Tone test", s, [id]);
        AppLog.Write("tone test", $"Test added: {steps} patches of {size:0.#} mm with {SourceLayer().Name}'s settings.");
        Close();
    }

    void Lists()
    {
        var strip = ToneTest.Strip(_doc.State);
        _pictures = [.. _doc.State.Shapes.OfType<ImageShape>().Where(p => strip is null || p.Id != strip.Id)];
        _picture.ItemsSource = _pictures.Select(p => string.IsNullOrWhiteSpace(p.Name) ? "Picture" : p.Name).Prepend("A grey gradient").ToList();
        _picture.SelectedIndex = _pictures.Count > 0 ? 1 : 0;
        _useLayers = [.. _doc.State.Layers.Where(l => l.Mode == LayerMode.Image && !ToneTest.IsTestLayer(l))];
        _useOn.ItemsSource = _useLayers.Select(l => l.Name).ToList();
        // The layer the test copied, if it's still there.
        var testLayer = _doc.State.Layers.FirstOrDefault(ToneTest.IsTestLayer);
        int match = testLayer is null ? -1 : _useLayers.FindIndex(l => ToneTest.LayerPrefix + l.Name == testLayer.Name);
        _useOn.SelectedIndex = match >= 0 ? match : _useLayers.Count > 0 ? 0 : -1;
        _material.ItemsSource = _lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
    }

    void ReadPhoto()
    {
        _patches.Children.Clear();
        _reading = null;
        var strip = ToneTest.Strip(_doc.State);
        var photo = _photo();
        if (strip is null) _photoNote.Text = "There's no tone test in this design yet: make one on the first tab.";
        else if (photo is null) _photoNote.Text = "No camera photo yet: press Update photo once the strip has been burned.";
        else
        {
            try { _reading = ToneTest.Read(strip, photo.Image, photo.Area, photo.MmPerPx); }
            catch (ArgumentException e) { _photoNote.Text = $"Couldn't read the strip: {e.Message}"; }
            if (_reading is null && string.IsNullOrEmpty(_photoNote.Text)) _photoNote.Text = "The photo doesn't show the whole strip: move the camera view or the strip, and update the photo.";
            else if (_reading is not null) _photoNote.Text = "";
            // A thumbnail of each patch with what was read.
            var patches = ToneTest.Patches(strip);
            for (int i = 0; i < patches.Count; i++)
            {
                Control face = new Border { Width = 56, Height = 56, Background = Brushes.Gray };
                if (MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, patches[i].Bed.Inflate(0.5), 80) is { } jpg)
                {
                    try { using var ms = new MemoryStream(jpg); face = new Image { Width = 56, Height = 56, Source = new Avalonia.Media.Imaging.Bitmap(ms), Stretch = Stretch.UniformToFill }; }
                    catch (ArgumentException) { }
                }
                string read = _reading is null ? "" : $"\n{_reading.Curve.Response(patches[i].Level) * 100:0}% light";
                _patches.Children.Add(new StackPanel
                {
                    Margin = new(3), Spacing = 2,
                    Children = { face, new TextBlock { Text = $"grey {patches[i].Level * 100:0}%{read}", FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } },
                });
            }
        }
        _chart.Show(_reading?.Curve);
        // A reading that can't be right is shown (so it can be looked at) but not used.
        string? problem = _reading is null ? null : ToneTest.Problem(_reading);
        if (problem is not null) _photoNote.Text = problem;
        bool usable = _reading is not null && problem is null;
        _apply.IsEnabled = usable && _useLayers.Count > 0;
        _save.IsEnabled = usable;
        Summary();
        Preview();
    }

    void Summary()
    {
        if (_reading is not { } r) { _summary.Text = ""; return; }
        var (darkFrom, firstShows) = r.Curve.Range();
        double mid = r.Curve.Response(0.5);
        var lines = new List<string>
        {
            $"Mid-grey comes out {mid * 100:0}% light (it should be 50%): pictures here burn {(mid < 0.42 ? "too dark" : mid > 0.58 ? "too pale" : "about right")} as they are.",
        };
        if (darkFrom > 0.05) lines.Add($"The material is as dark as it goes from {darkFrom * 100:0}% grey down: darker greys only add heat.");
        if (firstShows < 0.95) lines.Add($"Greys lighter than {firstShows * 100:0}% barely show.");
        // The lightest burned grey: if it already comes out dark, soft highlights can't be made.
        var lightest = r.Curve.Points.Where(p => p.Input < 0.999).OrderByDescending(p => p.Input).FirstOrDefault();
        if (lightest.Input > 0 && lightest.Lightness < 0.75)
            lines.Add($"Even the lightest burned grey comes out only {lightest.Lightness * 100:0}% light: there's a jump from bare wood to the first burn, so pale tones can't be made. Lower \"Lightest power %\" (or speed up) and test again.");
        lines.Add("Corrected, every tone is sent as the grey that gives it.");
        _summary.Text = string.Join(" ", lines);
    }

    void Preview()
    {
        RasterImage picture;
        int k = _picture.SelectedIndex;
        if (k > 0 && k - 1 < _pictures.Count) picture = Downsize(_pictures[k - 1].Source.Pixels, 320);
        else picture = Gradient();
        _original.Source = ToBitmap(picture);
        if (_reading is { } r)
        {
            _plainBurn.Source = ToBitmap(ToneTest.Simulate(picture, r.Curve, r.BareColour, r.BurnColour, corrected: false));
            _fixedBurn.Source = ToBitmap(ToneTest.Simulate(picture, r.Curve, r.BareColour, r.BurnColour, corrected: true));
        }
        else _plainBurn.Source = _fixedBurn.Source = null;
    }

    static RasterImage Gradient()
    {
        int w = 256, h = 64;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = (y * w + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = (byte)x;
                px[k + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    /// <summary>A copy no wider or taller than <paramref name="max"/> pixels (previews needn't be big).</summary>
    static RasterImage Downsize(RasterImage src, int max)
    {
        double k = Math.Min(1, (double)max / Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        if (w == src.Width && h == src.Height) return src;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(src.Width - 1, (int)(x / k)), sy = Math.Min(src.Height - 1, (int)(y / k));
                Array.Copy(src.Rgba, (sy * src.Width + sx) * 4, px, (y * w + x) * 4, 4);
            }
        return new RasterImage(w, h, px);
    }

    static Avalonia.Media.Imaging.Bitmap ToBitmap(RasterImage img)
    {
        using var ms = new MemoryStream(img.EncodePng());
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    void Apply()
    {
        if (_reading is not { } r || _useOn.SelectedIndex < 0 || _useOn.SelectedIndex >= _useLayers.Count) return;
        var layer = _useLayers[_useOn.SelectedIndex];
        _doc.Commit("Match pictures to the material", Ops.UpdateLayers(_doc.State, new HashSet<Guid> { layer.Id }, l => l with { MaterialTone = r.Curve }));
        _status.Text = $"\"{layer.Name}\" now corrects its pictures for this material. (Its settings must stay as tested for the correction to hold.)";
        _status.Foreground = Brush("Ok");
    }

    void Save()
    {
        if (_reading is not { } r) return;
        try
        {
            Material m;
            if (_material.SelectedIndex <= 0)
            {
                var name = _newName.Text?.Trim();
                if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; return; }
                m = new Material { Name = name, Category = "Other", HighlyReflective = Material.LooksReflective(name) };
            }
            else m = _lib.Materials[_material.SelectedIndex - 1];
            var testLayer = _doc.State.Layers.First(ToneTest.IsTestLayer);
            var settings = ProcessSettings.From(testLayer with { MaterialTone = r.Curve });
            var recipe = new Recipe
            {
                Purpose = RecipePurpose.Picture,
                Title = $"Pictures, matched ({Describe(testLayer)})",
                Settings = settings,
                Tested = true,
                TestedOn = DateTime.Now,
                Notes = _summary.Text ?? "",
            };
            _lib.Upsert(m with { Recipes = m.Recipes.Add(recipe) });
            _lib.Save();
            AppLog.Write("tone test", $"Saved \"{recipe.Title}\" to {m.Label} in {_lib.FilePath}.");
            _status.Text = $"Saved to {m.Label}: picking it for a picture layer brings the correction with it.";
            _status.Foreground = Brush("Ok");
            Lists();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Error("tone test save", e);
            _status.Text = $"Couldn't save: {e.Message}";
            _status.Foreground = Brush("Warn");
        }
    }

    /// <summary>The patches to be burned: black to white, with the grey of each written under it.</summary>
    sealed class WedgePreview : Control
    {
        IReadOnlyList<double> _levels = [];

        public void Show(IReadOnlyList<double> levels) { _levels = levels; InvalidateVisual(); }

        protected override Size MeasureOverride(Size available) => new(Math.Min(available.Width, 760), 74);

        public override void Render(DrawingContext ctx)
        {
            if (_levels.Count == 0) return;
            double w = Bounds.Width, cell = Math.Min(48, (w - 8) / (_levels.Count * 1.5 - 0.5)), gap = cell * 0.5;
            var text = ((ISolidColorBrush)Brush("TextMain")).Color;
            var muted = new SolidColorBrush(Color.FromArgb(170, text.R, text.G, text.B));
            // The bare material: a pale wood band behind the patches.
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#E6CFA6")), null, new Rect(0, 0, _levels.Count * (cell + gap) - gap + 8, cell + 8), 3, 3);
            for (int i = 0; i < _levels.Count; i++)
            {
                double x = 4 + i * (cell + gap);
                var c = Color.Parse("#E6CFA6");
                double g = _levels[i];
                // As sent: black is fully burned (dark brown), white is bare.
                var burn = Color.FromRgb((byte)(0x3A + (c.R - 0x3A) * g), (byte)(0x24 + (c.G - 0x24) * g), (byte)(0x16 + (c.B - 0x16) * g));
                ctx.DrawRectangle(new SolidColorBrush(burn), new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1), new Rect(x, 4, cell, cell));
                if (_levels.Count <= 21 || i % 2 == 0)
                {
                    var f = new FormattedText($"{g * 100:0}%", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 10, muted);
                    ctx.DrawText(f, new Point(x + cell / 2 - f.Width / 2, cell + 14));
                }
            }
        }
    }

    /// <summary>The measured response: grey sent along, how light it came out up; the ideal is the diagonal.</summary>
    sealed class ToneChart : Control
    {
        ToneCurve? _curve;

        public void Show(ToneCurve? curve) { _curve = curve; InvalidateVisual(); }

        public override void Render(DrawingContext ctx)
        {
            var text = ((ISolidColorBrush)Brush("TextMain")).Color;
            var muted = new SolidColorBrush(Color.FromArgb(170, text.R, text.G, text.B));
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(40, text.R, text.G, text.B)), 1);
            double l = 46, t = 10, r = Bounds.Width - 12, b = Bounds.Height - 30;
            if (r <= l + 20 || b <= t + 20) return;
            Point P(double x, double y) => new(l + (r - l) * x, b - (b - t) * y);
            void Write(string s, Point at, bool centre = true)
            {
                var f = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, muted);
                ctx.DrawText(f, new Point(at.X - (centre ? f.Width / 2 : f.Width + 4), at.Y - f.Height / 2));
            }
            for (int i = 0; i <= 4; i++)
            {
                double v = i / 4.0;
                ctx.DrawLine(grid, P(v, 0), P(v, 1));
                ctx.DrawLine(grid, P(0, v), P(1, v));
                Write($"{v * 100:0}%", P(v, 0) + new Point(0, 12));
                Write($"{v * 100:0}%", P(0, v), centre: false);
            }
            Write("grey sent →  (black … white)", new Point((l + r) / 2, Bounds.Height - 6));
            // The ideal: comes out as light as sent.
            ctx.DrawLine(new Pen(muted, 1.5, new DashStyle([4, 4], 0)), P(0, 0), P(1, 1));
            if (_curve is not { } c)
            {
                Write("No reading yet", new Point((l + r) / 2, (t + b) / 2));
                return;
            }
            var accent = UiKit.Brush("Accent");
            var m = c.Monotone;
            for (int i = 1; i < m.Count; i++) ctx.DrawLine(new Pen(accent, 2.5), P(m[i - 1].Input, m[i - 1].Lightness), P(m[i].Input, m[i].Lightness));
            foreach (var (x, y) in c.Points) ctx.DrawEllipse(accent, null, P(x, y), 4, 4);
            Write("how light it came out", P(0.02, 0.97) + new Point(70, 0));
        }
    }
}
