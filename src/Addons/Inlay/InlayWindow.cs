using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Inlay;

/// <summary>
/// Inlay and marquetry: the selected shapes as pieces of different woods (a wood per layer) that fit
/// together, and what they go into (a pocket, holes in a sheet, or nothing). Each wood is cut on a
/// page of its own with its kerf drawn in; a gap at every joint is shared between the two sides.
/// The fit test finds that gap. Opened with nothing selected, only the fit test can be made.
/// </summary>
public sealed class InlayWindow : Window
{
    /// <summary>A way to cut or engrave one material: its settings, and what they bring with them.</summary>
    sealed record Choice(string Label, Layer? Settings, double? KerfMm = null, double? EtchMmPerPass = null, double? ThicknessMm = null, Guid? Recipe = null);

    /// <summary>One material's row: its name, what it's cut with, its kerf.</summary>
    sealed class CutRow
    {
        public readonly TextBox Name = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly ComboBox With = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly NumericUpDown Kerf = Number(0, 1, 0.01m, "0.0##");
        public List<Choice> Choices = [];

        public Choice? Chosen => With.SelectedIndex >= 0 && With.SelectedIndex < Choices.Count ? Choices[With.SelectedIndex] : null;

        public WoodCut Cut(string otherwise) => new()
        {
            Name = string.IsNullOrWhiteSpace(Name.Text) ? otherwise : Name.Text.Trim(),
            KerfMm = (double)(Kerf.Value ?? 0),
            Settings = Chosen?.Settings,
        };
    }

    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly string? _dataFolder;
    readonly Action<string> _say;
    readonly InlayDesign _design;
    readonly Settings _settings;
    readonly List<Choice> _cutChoices, _etchChoices;
    readonly InlayPreview _preview = new() { MinWidth = 360, MinHeight = 320 };
    readonly ComboBox _into = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "A pocket engraved in a board", "Holes cut through a sheet", "Nothing: the pieces fit each other" },
    };
    readonly StackPanel _woods = new() { Spacing = 8 };
    readonly List<CutRow> _rows = [];
    readonly NumericUpDown _thick = Number(0.1m, 20, 0.05m, "0.0#"), _etch = Number(0.005m, 3, 0.005m, "0.0##");
    readonly ComboBox _etchWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CutRow _sheet = new();
    readonly CheckBox _edge = new() { Content = "Cut the sheet's outside edge too" };
    readonly NumericUpDown _margin = Number(0, 200, 1, "0.#");
    readonly NumericUpDown _fit = Number(-0.3m, 0.5m, 0.01m, "0.0##");
    readonly CheckBox _flip = new() { Content = "Turn the pieces over (cut them mirrored)" };
    readonly CheckBox _pack = new() { Content = "Pack the pieces close together" };
    readonly CheckBox _numbers = new() { Content = "Mark the pieces' numbers on them" };
    readonly Control _pocketPart, _sheetPart, _marginRow;
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _warn = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly Button _make = new() { Content = "Make the pieces", IsDefault = true, Classes = { "primary" }, MinWidth = 130 };
    bool _ready;

    /// <summary>What was made, for the status bar (null: nothing).</summary>
    public string? Made { get; private set; }

    /// <param name="dataFolder">Where the window remembers its settings (null: it doesn't).</param>
    public InlayWindow(Document doc, MaterialLibrary? lib, MachineProfile machine, string? dataFolder = null, Action<string>? say = null)
    {
        _doc = doc;
        _machine = machine;
        _dataFolder = dataFolder;
        _say = say ?? (_ => { });
        _settings = dataFolder is null ? new() : Settings.Load(dataFolder);
        _design = InlayDesign.From(doc.State, doc.Selection);
        var mats = lib?.Materials ?? [];
        _cutChoices = [.. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => new Choice(
            $"{m.Label} · {r.Title}" + (r.Settings.KerfMm > 0 ? $" (kerf {r.Settings.KerfMm:0.###} mm)" : ""),
            r.Settings.ApplyTo(Layer.Default(0, LayerMode.Cut)), r.Settings.KerfMm > 0 ? r.Settings.KerfMm : null, ThicknessMm: m.ThicknessMm, Recipe: r.Id)))];
        _etchChoices = [new Choice("Default engrave settings", null), .. mats.SelectMany(m => m.Recipes.Where(r => r.EtchMmPerPass is > 0).Select(r => new Choice(
            $"{m.Label} · {r.Title} ({r.EtchMmPerPass:0.###} mm a pass)", r.Settings.ApplyTo(Layer.Default(0, LayerMode.Fill)), EtchMmPerPass: r.EtchMmPerPass, Recipe: r.Id)))];

        Title = "Inlay and marquetry";
        Width = 960;
        Height = 680;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        // The woods: one row each (or one for the fit test's pieces when nothing is selected).
        var woodNames = _design.IsEmpty ? ["Inlay"] : _design.Woods.Select(w => w.Name).ToList();
        for (int i = 0; i < woodNames.Count; i++)
        {
            var row = new CutRow();
            var layer = _design.IsEmpty ? null : doc.State.FindLayer(_design.Woods[i].LayerId);
            row.Name.Text = woodNames[i];
            Fill(row, layer, _settings.WoodRecipes.TryGetValue(woodNames[i], out var id) ? id : null);
            _rows.Add(row);
            int count = _design.Pieces.Count(p => p.Wood == i);
            var swatch = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center,
                Background = _design.IsEmpty ? Brush("Line") : new SolidColorBrush(Color.FromRgb((byte)(_design.Woods[i].Color >> 16), (byte)(_design.Woods[i].Color >> 8), (byte)_design.Woods[i].Color)),
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, Muted(_design.IsEmpty ? "The pieces (for the fit test)" : $"{count} piece{(count == 1 ? "" : "s")}") } };
            _woods.Children.Add(new Border
            {
                BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        head,
                        Field("Wood", row.Name, tip: "What these pieces are cut from (it names their page). Each layer of the selected shapes is one wood."),
                        Field("Cut with", row.With, tip: "Their cut settings: a cut recipe from your library brings its kerf with it."),
                        Field("Kerf (mm)", row.Kerf, tip: "How wide the cut is in this wood (the Kerf gauge measures it). The cut lines go half of it outside each piece, so the piece comes out the drawn size."),
                    },
                },
            });
        }

        // The pocket.
        _etchWith.ItemsSource = _etchChoices.Select(c => c.Label).ToList();
        _etchWith.SelectedIndex = Math.Max(0, _etchChoices.FindIndex(c => c.Recipe is { } r && r == _settings.EtchRecipe) is var e && e >= 0 ? e : _etchChoices.Count > 1 ? 1 : 0);
        _etchWith.SelectionChanged += (_, _) => { if (EtchChoice()?.EtchMmPerPass is { } v) _etch.Value = (decimal)v; Changed(); };
        _thick.Value = (decimal)Math.Clamp(_settings.ThicknessMm, 0.1, 20);
        _etch.Value = (decimal)(EtchChoice()?.EtchMmPerPass ?? Math.Clamp(_settings.EtchMmPerPass, 0.005, 3));
        _pocketPart = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Pieces (mm)", _thick, tip: "How thick the pieces are (measure the veneer with calipers): the pocket goes this deep."),
                Field("Engrave with", _etchWith, tip: "The pocket's settings: an etch-rate recipe knows how deep a pass goes."),
                Field("Depth a pass (mm)", _etch, tip: "How deep one pass goes in the board. From an etch-rate test it's measured; without one it's a guess."),
            },
        };

        // The sheet.
        _sheet.Name.Text = _settings.SheetName;
        Fill(_sheet, null, _settings.SheetRecipe);
        _edge.IsChecked = _settings.CutSheetEdge;
        _margin.Value = (decimal)Math.Clamp(_settings.MarginMm, 0, 200);
        _marginRow = Field("Margin (mm)", _margin, tip: "How far the sheet's edge is from the picture.");
        _sheetPart = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Sheet", _sheet.Name, tip: "What the holes are cut in: the background veneer, or a board as thick as the pieces (a through inlay)."),
                Field("Cut with", _sheet.With, tip: "The sheet's cut settings."),
                Field("Kerf (mm)", _sheet.Kerf, tip: "How wide the cut is in the sheet: the holes' cut lines go half of it inside, so the holes come out the drawn size."),
                _edge,
                _marginRow,
            },
        };
        ToolTip.SetTip(_edge, "Ticked: the sheet is cut out round the picture too (a background veneer). Unticked: only the holes are cut, in a bigger board.");

        _fit.Value = (decimal)Math.Clamp(_settings.FitMm, -0.3, 0.5);
        _flip.IsChecked = _settings.Flip;
        _pack.IsChecked = _settings.Pack;
        _numbers.IsChecked = _settings.MarkNumbers;
        ToolTip.SetTip(_flip, "A laser cut is wider at the top, so each piece is a little bigger underneath. Cut mirrored and turned over, that bigger side shows: joints close up on the face, and pieces wedge into holes. Turn every piece over before putting it in.");
        ToolTip.SetTip(_pack, "Each wood's pieces in rows close together (never turned, so the grain runs the same way), instead of where they go in the picture. A key page shows where each number goes.");
        ToolTip.SetTip(_numbers, "Light marks of each piece's number on the side facing up when cut (the back, if they're turned over). Unticked, the numbers are only on screen.");
        _into.SelectedIndex = Math.Clamp((int)_settings.Into, 0, 2);

        var fitTest = Button("Fit test…", tip: "Holes (or pockets) at several gaps and stars to push into them: the snug one is the gap to use");
        fitTest.Click += (_, _) => FitTest();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        _make.Click += (_, _) => Make();
        _make.IsEnabled = !_design.IsEmpty;

        var side = new StackPanel
        {
            Spacing = 10,
            Width = 340,
            Children =
            {
                _summary,
                Field("Goes into", _into, tip: "A pocket: veneer inlaid into a board. Holes: a background veneer with the picture cut out of it (marquetry), or a board for a through inlay. Nothing: the pieces fit each other, to glue onto a backing."),
                _pocketPart,
                _sheetPart,
                new TextBlock { Text = "Woods", FontWeight = FontWeight.SemiBold },
                _woods,
                Field("Gap (mm)", _fit, tip: "The gap at every joint: between two pieces, and between a piece and the pocket or hole, shared half each side. 0.03–0.06 mm is snug for glue; 0 or below presses in tight. The fit test finds yours."),
                _flip,
                _pack,
                _numbers,
                _plan,
                _warn,
                Hint("Each wood's pieces go on a page of their own, cut with the kerf drawn in. Your design stays as it is, to show where everything goes."),
            },
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { fitTest, cancel, _make },
        };
        fitTest.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto"), ColumnSpacing = 16, RowSpacing = 12, Margin = new Thickness(16) };
        grid.Children.Add(new Border { Child = _preview, BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true });
        var scroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;

        foreach (var n in new[] { _thick, _etch, _margin, _fit }) n.ValueChanged += (_, _) => Changed();
        foreach (var row in _rows.Append(_sheet))
        {
            var r = row;
            r.With.SelectionChanged += (_, _) =>
            {
                if (r.Chosen?.KerfMm is { } k) r.Kerf.Value = (decimal)k;
                // Veneer of a known thickness: the pocket goes that deep.
                if (r == _rows.FirstOrDefault() && r.Chosen?.ThicknessMm is { } t && _ready) _thick.Value = (decimal)Math.Clamp(t, 0.1, 20);
                Changed();
            };
            r.Kerf.ValueChanged += (_, _) => Changed();
            r.Name.TextChanged += (_, _) => Changed();
        }
        foreach (var c in new[] { _edge, _flip, _pack, _numbers }) c.IsCheckedChanged += (_, _) => Changed();
        // Holes in a board (no edge cut) are a through inlay: turned over, the pieces wedge in.
        _edge.IsCheckedChanged += (_, _) => { if (_ready && Into() == InlayInto.Holes) _flip.IsChecked = !(_edge.IsChecked ?? true); };
        _into.SelectionChanged += (_, _) =>
        {
            // Through a thick board the taper matters: turned over, the pieces wedge in.
            if (_ready) _flip.IsChecked = Into() == InlayInto.Holes && !(_edge.IsChecked ?? true);
            Changed();
        };
        _ready = true;
        Changed();
    }

    /// <summary>Fill a row's "Cut with" list: the layer's own settings (when it's a cut layer), the default, the library's cut recipes.</summary>
    void Fill(CutRow row, Layer? layer, Guid? remembered)
    {
        var choices = new List<Choice>();
        if (layer is { Mode: LayerMode.Cut })
            choices.Add(new Choice($"This layer's settings ({layer.Name})", layer, layer.KerfMm > 0 ? layer.KerfMm : null));
        choices.Add(new Choice("Default cut settings", null));
        choices.AddRange(_cutChoices);
        row.Choices = choices;
        row.With.ItemsSource = choices.Select(c => c.Label).ToList();
        int pick = remembered is { } id ? choices.FindIndex(c => c.Recipe == id) : -1;
        row.With.SelectedIndex = pick >= 0 ? pick : 0;
        row.Kerf.Value = (decimal)Math.Clamp(row.Chosen?.KerfMm ?? _settings.KerfMm, 0, 1);
    }

    Choice? EtchChoice() => _etchWith.SelectedIndex >= 0 && _etchWith.SelectedIndex < _etchChoices.Count ? _etchChoices[_etchWith.SelectedIndex] : null;

    InlayInto Into() => (InlayInto)Math.Clamp(_into.SelectedIndex, 0, 2);

    /// <summary>The window's settings as the maker's.</summary>
    InlayOptions Options() => new()
    {
        Into = Into(),
        FitMm = (double)(_fit.Value ?? 0.05m),
        ThicknessMm = (double)(_thick.Value ?? 0.6m),
        EtchMmPerPass = (double)(_etch.Value ?? 0.15m),
        PocketSettings = EtchChoice()?.Settings,
        Sheet = _sheet.Cut("Background"),
        CutSheetEdge = _edge.IsChecked ?? true,
        MarginMm = (double)(_margin.Value ?? 10),
        Flip = _flip.IsChecked ?? false,
        Pack = _pack.IsChecked ?? false,
        MarkNumbers = _numbers.IsChecked ?? false,
        Woods = [.. _rows.Select((r, i) => r.Cut(_design.IsEmpty ? "Inlay" : _design.Woods[i].Name))],
    };

    void Changed()
    {
        if (!_ready) return;
        var o = Options();
        _pocketPart.IsVisible = o.Into == InlayInto.Pocket;
        _sheetPart.IsVisible = o.Into == InlayInto.Holes;
        _marginRow.IsVisible = o.CutSheetEdge;
        _preview.Show(_design, o, [.. o.Woods.Select(w => w.Name)]);
        var warn = new List<string>();
        if (_design.IsEmpty)
        {
            _summary.Text = "Nothing is selected: select the closed shapes to inlay (each layer is one wood) and open this again. The fit test can be made now.";
            _plan.Text = "";
        }
        else
        {
            int n = _design.Pieces.Count, w = _design.Woods.Count;
            _summary.Text = $"{n} piece{(n == 1 ? "" : "s")} in {w} wood{(w == 1 ? "" : "s")}, {_design.Bounds.Width:0.#} × {_design.Bounds.Height:0.#} mm. Shapes on top cut away what's under them, as on the canvas.";
            var lines = new List<string>();
            if (o.Into == InlayInto.Pocket)
            {
                int passes = InlayMaker.Passes(o);
                lines.Add($"Pocket: {passes} pass{(passes == 1 ? "" : "es")} of {o.EtchMmPerPass:0.###} mm, about {passes * o.EtchMmPerPass:0.##} mm deep for {o.ThicknessMm:0.##} mm pieces.");
                if (EtchChoice()?.EtchMmPerPass is null) warn.Add("The depth a pass is a guess without an etch-rate test (Materials & Tests → Tests → Etch rate): the pieces may sit proud or sunk.");
            }
            else if (o.Into == InlayInto.Holes)
                lines.Add($"Holes in the {(string.IsNullOrWhiteSpace(o.Sheet.Name) ? "sheet" : o.Sheet.Name.Trim())}" + (o.CutSheetEdge ? ", cut out with a margin of " + $"{o.MarginMm:0.#} mm." : ", only the holes cut."));
            lines.Add($"Pages: {w + (o.Into == InlayInto.Nothing ? 0 : 1) + (o.Pack ? 1 : 0)} new, each wood on its own.");
            _plan.Text = string.Join("\n", lines);
            try
            {
                var r = InlayMaker.Make(_doc.State, _design, o, _machine.FieldW);
                warn.AddRange(r.Warnings);
            }
            catch (InvalidOperationException e) { warn.Add(e.Message); }
            if (_design.Open > 0) warn.Add($"{_design.Open} selected line{(_design.Open == 1 ? " has" : "s have")} no closed outline, so {(_design.Open == 1 ? "isn't a piece" : "aren't pieces")}.");
        }
        if (o.FitMm < -0.15) warn.Add("A gap this far below 0 makes pieces too big to go in: wood squashes a little, not much.");
        _warn.Text = string.Join("\n", warn);
        _warn.Foreground = Brush("Warn");
    }

    void Remember()
    {
        if (_dataFolder is null) return;
        var o = Options();
        _settings.Into = o.Into;
        _settings.FitMm = o.FitMm;
        _settings.ThicknessMm = o.ThicknessMm;
        _settings.EtchMmPerPass = o.EtchMmPerPass;
        _settings.EtchRecipe = EtchChoice()?.Recipe;
        _settings.SheetName = _sheet.Name.Text ?? "Background";
        _settings.SheetRecipe = _sheet.Chosen?.Recipe;
        _settings.CutSheetEdge = o.CutSheetEdge;
        _settings.MarginMm = o.MarginMm;
        _settings.Flip = o.Flip;
        _settings.Pack = o.Pack;
        _settings.MarkNumbers = o.MarkNumbers;
        if (_rows.Count > 0) _settings.KerfMm = (double)(_rows[0].Kerf.Value ?? 0.1m);
        for (int i = 0; i < _rows.Count && !_design.IsEmpty; i++)
            if (_rows[i].Chosen?.Recipe is { } id) _settings.WoodRecipes[_design.Woods[i].Name] = id;
        _settings.Save(_dataFolder);
    }

    void Make()
    {
        try
        {
            var r = InlayMaker.Make(_doc.State, _design, Options(), _machine.FieldW);
            _doc.Commit("Inlay", r.State);
            Remember();
            Made = $"Made {r.Pages.Count} page{(r.Pages.Count == 1 ? "" : "s")}: {ListWords(r.Pages)}. Your design is on the page before them.";
            _say(Made);
            Close();
        }
        catch (InvalidOperationException e) { _warn.Text = e.Message; }
    }

    void FitTest()
    {
        var o = Options();
        var test = InlayFitTest.Make(_doc.State, new FitTestOptions { Into = o.Into, Inlay = o, Piece = o.Woods.Count > 0 ? o.Woods[0] : new WoodCut() });
        _doc.Commit("Inlay fit test", test.State);
        Remember();
        Made = $"Made the fit test on {test.Pages.Count} pages: {ListWords(test.Pages)}. Burn and cut both, push a star into each hole, and type the snuggest one's gap into Gap.";
        _say(Made);
        Close();
    }

    /// <summary>What the window remembers between uses.</summary>
    sealed class Settings
    {
        public InlayInto Into { get; set; } = InlayInto.Pocket;
        public double FitMm { get; set; } = 0.05;
        public double ThicknessMm { get; set; } = 0.6;
        public double EtchMmPerPass { get; set; } = 0.15;
        public Guid? EtchRecipe { get; set; }
        public string SheetName { get; set; } = "Background";
        public Guid? SheetRecipe { get; set; }
        public bool CutSheetEdge { get; set; } = true;
        public double MarginMm { get; set; } = 10;
        public bool Flip { get; set; }
        public bool Pack { get; set; }
        public bool MarkNumbers { get; set; }
        public double KerfMm { get; set; } = 0.1;
        public Dictionary<string, Guid> WoodRecipes { get; set; } = [];

        static readonly JsonSerializerOptions Json = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };

        static string PathIn(string folder) => Path.Combine(folder, "settings.json");

        public static Settings Load(string folder)
        {
            try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathIn(folder)), Json) ?? new(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return new(); }
        }

        public void Save(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(PathIn(folder), JsonSerializer.Serialize(this, Json));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
