using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.Addon.RecipeTuner;

/// <summary>
/// The recipe tuner: pick a material and what it's for, see what the library already says, get the
/// next few settings worth trying, put them in the design as a small round of tries, and after
/// burning them, rate each one. The ratings go back into what the tuner learns from.
/// </summary>
public sealed class RecipeTunerWindow : Window
{
    readonly IAddonHost _host;
    readonly TriesStore _store;
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _purpose = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Knobs.Purposes.Select(PurposeName).ToList() };
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Blue (diode)", "Red (MOPA fiber)" } };
    readonly Control _lightRow;
    readonly WrapPanel _knobs = new() { Orientation = Orientation.Horizontal };
    readonly NumericUpDown _count = Number(2, 12, 1, "0");
    readonly NumericUpDown _size = Number(3, 40, 1, "0");
    readonly TextBlock _knows = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _known = new() { Spacing = 2 };
    readonly StackPanel _suggestions = new() { Spacing = 6 };
    readonly TextBlock _suggestStatus = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _add = new() { Content = "Add the tries to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _suggest;
    readonly TabControl _tabs = new();

    readonly StackPanel _rateRows = new() { Spacing = 6 };
    readonly TextBlock _rateHeader = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _rateStatus = new() { TextWrapping = TextWrapping.Wrap };
    readonly CheckBox _keepPoor = new() { Content = "Also keep poor tries (1 and 2 stars) in the library" };
    readonly TextBox _notes = new() { Watermark = "Notes for the saved tries (optional)" };
    readonly Dictionary<int, Stars> _stars = [];
    readonly Dictionary<int, ComboBox> _heat = [];
    readonly Button _saveRatings = new() { Content = "Save the ratings", Classes = { "primary" }, MinWidth = 160 };

    TuneResult? _result;
    bool _loading;
    int _busy;

    public RecipeTunerWindow(IAddonHost host, TriesStore store, bool rate)
    {
        _host = host;
        _store = store;
        Title = "Recipe tuner";
        Width = 780;
        Height = 800;
        MinWidth = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _count.Value = 6;
        _size.Value = 10;
        _lightRow = Field("Laser", _light);
        _lightRow.IsVisible = host.Machine.Mopa.Count > 0;

        _suggest = Button("Suggest", "IcBulb", "Work out the next tries from every rating the library has for this");
        _suggest.Click += (_, _) => _ = Recompute();
        _add.Click += (_, _) => AddTries();
        _material.SelectionChanged += (_, _) => Changed();
        _purpose.SelectionChanged += (_, _) => { if (!_loading) { BuildKnobs(); Changed(); } };
        _light.SelectionChanged += (_, _) => { if (!_loading) { BuildKnobs(); Changed(); } };
        _count.ValueChanged += (_, _) => Changed();

        var suggest = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 16, 0),
            Children =
            {
                Hint("The tuner learns from the stars on your recipes: this material's own, and (counting for less) those on similar materials of the same category. It suggests the next settings to try: its best guess first, then ones that could beat your best so far. Burn them, rate them, and ask again: each round gets closer."),
                Field("Material", _material),
                Field("What for", _purpose),
                _lightRow,
                Explained("What it knows", "Rated recipes for this material and work. Rate more in Laser → Library, or with Record results after a test grid.", _knows, _known),
                Field("Change", _knobs, tip: "The settings the tuner may change; the others stay as in the best recipe so far."),
                Field("Tries", _count, tip: "How many settings to suggest in this round (2–12)."),
                _suggest,
                _suggestions,
                _suggestStatus,
                Field("Size (mm)", _size, tip: "Each try is a square this big (pictures: a copy of the selected picture this big)."),
                _add,
                Hint("Adds one small square per try, each on its own layer (“Test tune 1”, “Test tune 2” …) with those settings, numbered on a light score layer. Takes out an earlier round's tries first. Nothing is burned until you send it with Ship It."),
            },
        };

        var update = Button("Update photo", "IcCamera", "Take a fresh camera photo of the bed, to see each try here (nothing moves)");
        update.Click += async (_, _) =>
        {
            update.IsEnabled = false;
            _rateStatus.Text = "Taking a photo…";
            try { _rateStatus.Text = await _host.TakePhotoAsync() is null ? "No photo: connect the laser and calibrate the camera first." : "Rate each try."; }
            finally { update.IsEnabled = true; }
            BuildRate();
        };
        _saveRatings.Click += (_, _) => SaveRatings();
        var next = Button("Suggest the next round", "IcBulb", "Back to Suggest, with these ratings counted");
        next.Click += (_, _) => NextRound();
        var remove = Button("Take the tries out of the design", "IcTrash", "Remove the tries, their layers and their numbers (undo brings them back)");
        remove.Click += (_, _) =>
        {
            if (!TuneTries.InDesign(_host.Document.State)) return;
            _host.Document.Commit("Remove recipe tuner tries", TuneTries.Remove(_host.Document.State));
            BuildRate();
        };
        var rateTab = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 16, 0),
            Children =
            {
                Hint("After burning the tries, give each one stars: 5 = perfect, 3 = good, 1 = poor. Leave a try at no stars to skip it. Poor ratings matter too: they show the tuner where not to go. Say whether each had too little or too much heat as well: that tells it which way to go, so it gets there in fewer rounds."),
                _rateHeader,
                update,
                _rateRows,
                _notes,
                _keepPoor,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _saveRatings } },
                _rateStatus,
                Row(next, remove),
            },
        };
        _tabs.Items.Add(new TabItem { Header = "Suggest", Content = new ScrollViewer { Content = suggest } });
        _tabs.Items.Add(new TabItem { Header = "Rate the tries", Content = new ScrollViewer { Content = rateTab } });
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(_tabs);
        Content = new Border { Padding = new Thickness(16), Child = root };

        FillMaterials();
        BuildKnobs();
        BuildRate();
        _tabs.SelectedIndex = rate && TuneTries.InDesign(host.Document.State) ? 1 : 0;
        _ = Recompute();
    }

    static string PurposeName(RecipePurpose p) => p switch
    {
        RecipePurpose.Cut => "Cut through",
        RecipePurpose.Score => "Score (a line on the surface)",
        RecipePurpose.Engrave => "Engrave (fill)",
        RecipePurpose.Mark => "Mark (metal, fill)",
        _ => "Picture",
    };

    Material? CurrentMaterial => _material.SelectedIndex >= 0 && _material.SelectedIndex < _host.Library.Materials.Count ? _host.Library.Materials[_material.SelectedIndex] : null;
    RecipePurpose CurrentPurpose => Knobs.Purposes[Math.Clamp(_purpose.SelectedIndex, 0, Knobs.Purposes.Count - 1)];
    LightSource CurrentLight => _light.SelectedIndex == 1 && _host.Machine.Mopa.Count > 0 ? LightSource.Red : LightSource.Blue;

    /// <summary>Start from the active layer's material, kind of work and laser (or the round in the design).</summary>
    void FillMaterials()
    {
        _loading = true;
        var lib = _host.Library;
        _material.ItemsSource = lib.Materials.Select(m => m.Label).ToList();
        var doc = _host.Document;
        var layer = doc.State.Layers.FirstOrDefault(l => l.Id == doc.ActiveLayer);
        var round = TuneTries.Round(doc.State);
        Guid? mat = round?.MaterialId ?? layer?.Material?.MaterialId;
        int mi = mat is { } id ? lib.Materials.FindIndex(m => m.Id == id) : -1;
        _material.SelectedIndex = mi >= 0 ? mi : lib.Materials.Count > 0 ? 0 : -1;
        var purpose = round?.Purpose ?? layer?.Mode switch
        {
            LayerMode.Cut => RecipePurpose.Cut,
            LayerMode.Score => RecipePurpose.Score,
            LayerMode.Image => RecipePurpose.Picture,
            LayerMode.Fill => layer.Laser.Light == LightSource.Red ? RecipePurpose.Mark : RecipePurpose.Engrave,
            _ => RecipePurpose.Cut,
        };
        _purpose.SelectedIndex = Math.Max(0, Knobs.Purposes.ToList().IndexOf(purpose));
        _light.SelectedIndex = (round?.Light ?? layer?.Laser.Light) == LightSource.Red && _host.Machine.Mopa.Count > 0 ? 1 : 0;
        _loading = false;
    }

    void BuildKnobs()
    {
        var keep = _knobs.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (Knob)c.Tag!).ToHashSet();
        _knobs.Children.Clear();
        var all = Knobs.For(CurrentPurpose, CurrentLight);
        var on = keep.Count > 0 && keep.All(all.Contains) ? keep : Knobs.Default(CurrentPurpose, CurrentLight).ToHashSet();
        foreach (var k in all)
        {
            var c = new CheckBox { Content = Knobs.Name(k), Tag = k, IsChecked = on.Contains(k), Margin = new(0, 0, 14, 0) };
            ToolTip.SetTip(c, k switch
            {
                Knob.Focus => "Focus offset: + focuses deeper into the material (the head goes lower)",
                Knob.Frequency => "The fiber's pulse frequency (kept to what its pulse width allows)",
                Knob.PulseWidth => "The fiber's pulse width (one of those the source has)",
                _ => $"Let the tuner change {Knobs.Name(k).ToLowerInvariant()}; unticked settings stay as in the best recipe so far",
            });
            c.IsCheckedChanged += (_, _) => Changed();
            _knobs.Children.Add(c);
        }
    }

    IReadOnlyList<Knob> Change() => [.. _knobs.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (Knob)c.Tag!)];

    void Changed()
    {
        if (_loading) return;
        _ = Recompute();
    }

    async Task Recompute()
    {
        var mat = CurrentMaterial;
        _suggestions.Children.Clear();
        _result = null;
        _add.IsEnabled = false;
        if (mat is null)
        {
            _knows.Text = "Your material library is empty. Add a material in Laser → Library (or burn a Laser → Test grid and Record results), then come back.";
            _known.Children.Clear();
            return;
        }
        var ask = new TuneAsk(mat, CurrentPurpose, CurrentLight, Change(), (int)(_count.Value ?? 6));
        var machine = _host.Machine;
        var evidence = Tuner.Gather(_host.Library, _store.Tries, mat, ask.Purpose, ask.Light, machine.DeviceCode);
        ShowKnown(mat, evidence, ask);
        int ticket = ++_busy;
        _suggestStatus.Text = "Working it out…";
        var result = await Task.Run(() => Tuner.Suggest(ask, evidence, machine));
        if (ticket != _busy) return; // something changed meanwhile
        _result = result;
        ShowSuggestions(ask);
    }

    void ShowKnown(Material mat, List<Rated> ev, TuneAsk ask)
    {
        _known.Children.Clear();
        var own = ev.Where(e => e.Own).ToList();
        var others = ev.Where(e => !e.Own).GroupBy(e => e.From).Select(g => $"{g.Key} ({g.Count()})").ToList();
        string what = PurposeName(ask.Purpose).ToLowerInvariant();
        _knows.Text = (own.Count, others.Count) switch
        {
            (0, 0) => $"Nothing rated yet for {what} on {mat.Label} or similar materials. The first round is a spread around the starting point; rate it and the tuner takes it from there.",
            (0, _) => $"Nothing rated on {mat.Label} itself yet. It borrows from similar materials, counting them for less: {ListWords(others)}.",
            _ => $"{own.Count} rated on {mat.Label}" + (others.Count > 0 ? $"; similar materials count for less: {ListWords(others)}." : "."),
        };
        foreach (var e in own.OrderByDescending(e => e.Rating).ThenByDescending(e => e.When).Take(8))
            _known.Children.Add(new TextBlock { Text = $"{Stars.Text(e.Rating)}   {Knobs.Summary(e.Settings, ask.Purpose)}", FontSize = 12 });
        if (own.Count > 8) _known.Children.Add(Muted($"… and {own.Count - 8} more."));
    }

    void ShowSuggestions(TuneAsk ask)
    {
        _suggestions.Children.Clear();
        if (_result is not { } r || r.Suggestions.Count == 0)
        {
            _suggestStatus.Text = "No new settings to suggest: every setting in reach has been tried. Tick another setting under Change.";
            return;
        }
        for (int i = 0; i < r.Suggestions.Count; i++)
            _suggestions.Children.Add(SuggestionRow(i + 1, r.Suggestions[i], ask));
        _suggestStatus.Text = $"Starting from {r.StartFrom}. Expected stars are the tuner's guess (± how sure it is); the round is meant to be burned together.";
        _add.IsEnabled = ask.Purpose != RecipePurpose.Picture || _host.SelectedPicture is not null;
        if (!_add.IsEnabled) _suggestStatus.Text += " For pictures, select the picture to try them on first.";
    }

    Control SuggestionRow(int n, Suggestion s, TuneAsk ask)
    {
        string kind = s.Kind switch
        {
            SuggestionKind.BestGuess => "Best guess",
            SuggestionKind.Refine => "Fine-tune",
            SuggestionKind.Explore => "Explore",
            _ => "Spread",
        };
        string expect = double.IsNaN(s.Expected) ? "" : $"expect {s.Expected:0.0} ★ ± {s.Uncertainty:0.0}";
        var use = new Button { Content = "Use on layer", VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(use, "Put these settings on the active layer (one undo step)");
        use.Click += (_, _) => UseOnLayer(s.Settings, ask);
        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = $"{n}.  {Knobs.Summary(s.Settings, ask.Purpose)}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = expect.Length > 0 ? $"{kind} · {expect}" : kind, Classes = { "muted" }, FontSize = 12 },
                Hint(s.Why).With(h => h.TextWrapping = TextWrapping.Wrap),
            },
        };
        var g = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        g.Children.Add(text);
        Grid.SetColumn(use, 1);
        g.Children.Add(use);
        return new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), Padding = new(10, 6), Child = g };
    }

    void UseOnLayer(ProcessSettings s, TuneAsk ask)
    {
        var doc = _host.Document;
        if (doc.State.Layers.FirstOrDefault(l => l.Id == doc.ActiveLayer) is not { } layer) return;
        var mref = new MaterialRef(ask.Material.Id, ask.Material.Label);
        doc.Commit("Recipe tuner settings", Ops.UpdateLayers(doc.State, new HashSet<Guid> { layer.Id },
            l => (s with { Mode = Knobs.ModeFor(ask.Purpose) }).ApplyTo(l) with { Material = mref, Reflective = ask.Material.HighlyReflective }));
        _host.Status($"{layer.Name}: {Knobs.Summary(s, ask.Purpose)}");
    }

    void AddTries()
    {
        if (_result is not { } r || CurrentMaterial is not { } mat || r.Suggestions.Count == 0) return;
        var doc = _host.Document;
        var (state, added, _) = TuneTries.Add(doc.State, mat, CurrentPurpose, CurrentLight, [.. r.Suggestions.Select(x => x.Settings)],
            (double)(_size.Value ?? 10), _host.AppSettings.TextFont, _host.Machine, _host.SelectedPicture);
        doc.Commit("Recipe tuner tries", state, added);
        AppLog.Write("tuner", $"{r.Suggestions.Count} tries added for {mat.Label} ({CurrentPurpose}).");
        _host.FitWorkArea();
        _host.Status($"{r.Suggestions.Count} tries added. Burn them with Ship It; the tuner opens to rate them when the job finishes.");
        Close();
    }

    // ---- rating -------------------------------------------------------------------------------

    void BuildRate()
    {
        _rateRows.Children.Clear();
        _stars.Clear();
        _heat.Clear();
        var s = _host.Document.State;
        var round = TuneTries.Round(s);
        var tries = TuneTries.Read(s);
        _saveRatings.IsEnabled = tries.Count > 0 && round is not null;
        if (tries.Count == 0 || round is null)
        {
            _rateHeader.Text = "There are no tries in this design. Make a round on the Suggest tab, burn it, then rate it here.";
            return;
        }
        _rateHeader.Text = $"{tries.Count} tries on {round.MaterialLabel} for {PurposeName(round.Purpose).ToLowerInvariant()}, made {round.Made:d MMM HH:mm}.";
        var photo = _host.Photo;
        foreach (var t in tries)
        {
            Control face = new TextBlock { Text = t.Number.ToString(), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, t.Bounds.Inflate(1.5), 160) is { } jpg)
            {
                try { using var ms = new MemoryStream(jpg); face = new Image { Source = new Bitmap(ms), Stretch = Stretch.UniformToFill }; }
                catch (ArgumentException) { }
            }
            var kept = _store.Find(round.Id, t.Number);
            var stars = new Stars { Value = kept?.Rating ?? 0 };
            _stars[t.Number] = stars;
            var heat = new ComboBox { ItemsSource = HeatWords(round.Purpose), SelectedIndex = (kept?.Heat ?? 0) switch { < 0 => 1, > 0 => 3, _ => kept is null ? 0 : 2 }, Width = 215, Margin = new(0, 4, 0, 0) };
            ToolTip.SetTip(heat, "Not enough or too much: this tells the tuner which way to go, and makes it find the settings much sooner");
            _heat[t.Number] = heat;
            var settings = ProcessSettings.From(t.Layer);
            var g = new Grid { ColumnDefinitions = new("72,*,Auto"), ColumnSpacing = 10 };
            g.Children.Add(new Border { Width = 64, Height = 64, ClipToBounds = true, CornerRadius = new(4), Background = Brush("Bg2"), Child = face });
            var label = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Spacing = 2,
                Children =
                {
                    new TextBlock { Text = $"Try {t.Number}", FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = Knobs.Summary(settings, round.Purpose), TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                },
            };
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { stars, heat } };
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            _rateRows.Children.Add(g);
        }
    }

    /// <summary>The heat choices for a try, in the words for the work.</summary>
    static string[] HeatWords(RecipePurpose p) => p switch
    {
        RecipePurpose.Cut => ["How was the heat?", "Didn't cut through", "Cut cleanly", "Too much (charred, wide)"],
        RecipePurpose.Score => ["How was the heat?", "Too faint", "About right", "Too deep or burnt"],
        _ => ["How was the heat?", "Too light", "About right", "Too dark or burnt"],
    };

    void SaveRatings()
    {
        var s = _host.Document.State;
        if (TuneTries.Round(s) is not { } round) return;
        var lib = _host.Library;
        var mat = lib.Find(round.MaterialId);
        if (mat is null) { _rateStatus.Text = $"{round.MaterialLabel} isn't in the library any more."; return; }
        var photo = _host.Photo;
        int rated = 0, saved = 0;
        var recipes = mat.Recipes;
        foreach (var t in TuneTries.Read(s))
        {
            if (!_stars.TryGetValue(t.Number, out var st) || st.Value <= 0) continue;
            rated++;
            var settings = ProcessSettings.From(t.Layer);
            var before = _store.Find(round.Id, t.Number);
            // Saved again: the earlier library copy is replaced.
            if (before?.RecipeId is { } old) recipes = recipes.RemoveAll(x => x.Id == old);
            Guid? recipeId = null;
            if (st.Value >= 3 || _keepPoor.IsChecked == true)
            {
                var recipe = new Recipe
                {
                    Id = before?.RecipeId ?? Guid.NewGuid(),
                    Purpose = round.Purpose,
                    Title = $"Tuner try {t.Number}: {Knobs.Summary(settings, round.Purpose)}",
                    Settings = settings,
                    Rating = st.Value,
                    Tested = true,
                    TestedOn = DateTime.Now,
                    FocusMm = _host.FocusDistanceMm,
                    Notes = _notes.Text?.Trim() ?? "",
                    Machine = _host.Machine.DeviceCode,
                };
                if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, t.Bounds.Inflate(1.5)) is { } jpg)
                    recipe = recipe with { Photo = lib.SavePhoto(recipe.Id, jpg) };
                recipes = recipes.Add(recipe);
                recipeId = recipe.Id;
                saved++;
            }
            int heat = _heat.TryGetValue(t.Number, out var hb) ? hb.SelectedIndex switch { 1 => -1, 3 => 1, _ => 0 } : 0;
            _store.Put(new TriedRecord(round.Id, t.Number, mat.Id, mat.Name, mat.Category, mat.ThicknessMm, round.Purpose, settings, st.Value, DateTime.Now, _host.Machine.DeviceCode, recipeId, heat));
        }
        if (rated == 0) { _rateStatus.Text = "Give at least one try some stars first."; return; }
        lib.Upsert(mat with { Recipes = recipes });
        lib.Save();
        _store.Save();
        _host.RefreshLibrary();
        AppLog.Write("tuner", $"{rated} tries rated on {mat.Label}, {saved} saved as recipes.");
        _rateStatus.Text = $"Saved {rated} rating{(rated == 1 ? "" : "s")}." + (saved > 0 ? $" {saved} went into {mat.Label} as tested recipes." : "") +
                           (rated > saved ? $" {rated - saved} poor one{(rated - saved == 1 ? "" : "s")} kept by the tuner only." : "");
        _rateStatus.Foreground = Brush("Ok");
    }

    void NextRound()
    {
        FillMaterials();
        BuildKnobs();
        _tabs.SelectedIndex = 0;
        _ = Recompute();
    }
}
