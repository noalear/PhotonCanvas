using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The floor and stairs maker: the room of walls the floor sits on (read from the wall maker's walls
/// in the design), the floor plate, the opening in it and the way up, with the whole thing put
/// together in 3D or as the pieces to cut; then the pieces go into the design, nested onto the bed.
/// </summary>
public sealed class StairsWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    readonly CustomTextures _own;
    (Guid Id, FloorSpec Spec)? _editing;

    // Room
    readonly NumericUpDown _wallH = Number(5, 1000, 1, "0.#"), _wallSheet = Number(0.5m, 20, 0.5m, "0.##"), _wallLen = Number(10, 3000, 0.5m, "0.##"), _wallWid = Number(10, 3000, 0.5m, "0.##");
    readonly NumericUpDown _along = Number(1, 20, 1, "0"), _across = Number(1, 20, 1, "0"), _colW = Number(3, 200, 0.5m, "0.#");
    readonly CheckBox _twoSided = new() { Content = "The walls are two panels back to back" };
    readonly CheckBox _columns = new() { Content = "The walls are joined by columns", IsChecked = true }, _caps = new() { Content = "The columns have caps", IsChecked = true };
    readonly ComboBox _colShape = Combo("Auto (square)", "Square", "Octagon", "Round");
    readonly TextBlock _matchNote = Hint();
    readonly NumericUpDown _sheet = Number(0.5m, 20, 0.5m, "0.##"), _fit = Number(-1, 2, 0.05m, "0.##");
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];
    Control _columnRows = null!;

    // Floor
    readonly CheckBox _floor = new() { Content = "Make a floor", IsChecked = true };
    readonly ComboBox _edge = Combo("Over the columns (a little past them)", "Flush with the walls", "Overhanging the walls");
    readonly NumericUpDown _overhang = Number(0, 100, 0.5m, "0.#"), _floors = Number(1, 20, 1, "0"), _piecesAlong = Number(1, 6, 1, "0"), _piecesAcross = Number(1, 6, 1, "0");
    readonly ComboBox _texture = Combo();
    readonly List<string> _textureNames = [];
    readonly NumericUpDown _sizeA = Number(0.5m, 300, 0.5m, "0.##"), _sizeB = Number(0.5m, 300, 0.5m, "0.##"), _groove = Number(0.1m, 3, 0.1m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly TextBlock _sizeALabel = Label("Size"), _sizeBLabel = Label("Size"), _textureAbout = Hint();
    Control _sizeARow = null!, _sizeBRow = null!, _grooveRow = null!, _seedRow = null!, _overhangRow = null!, _floorRows = null!;
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly CheckBox _across2 = new() { Content = "Turn the texture a quarter turn (planks across the room)" };

    // Opening
    readonly CheckBox _hatch = new() { Content = "An opening for the way up", IsChecked = true }, _hatchAuto = new() { Content = "Size and place it to suit the way up", IsChecked = true };
    readonly NumericUpDown _hatchX = Number(-500, 3000, 0.5m, "0.#"), _hatchY = Number(-500, 3000, 0.5m, "0.#"), _hatchL = Number(4, 1000, 0.5m, "0.#"), _hatchW = Number(4, 1000, 0.5m, "0.#");
    readonly CheckBox _trapdoor = new() { Content = "A loose trapdoor to cover it" };
    Control _hatchRows = null!, _hatchManual = null!;

    // Way up
    readonly ComboBox _way = Combo("Nothing", "Open stairs", "Solid stairs (stacked layers)", "Spiral stairs", "Leaning ladder", "Hooked ladder");
    readonly NumericUpDown _climb = Number(5, 1000, 1, "0.#"), _ways = Number(1, 20, 1, "0");
    readonly NumericUpDown _stairW = Number(5, 300, 1, "0.#"), _rise = Number(1, 100, 0.5m, "0.#"), _going = Number(3, 60, 0.5m, "0.#");
    readonly ComboBox _rail = Combo("No handrail", "On the left", "On the right", "Both sides");
    readonly CheckBox _treadTex = new() { Content = "Etch the floor's texture on the steps", IsChecked = true };
    readonly NumericUpDown _spiralR = Number(10, 200, 1, "0.#"), _spiralStep = Number(10, 90, 5, "0.#"), _hub = Number(4, 100, 0.5m, "0.#"), _spiralPin = Number(1, 10, 0.5m, "0.#");
    readonly ComboBox _turn = Combo("Clockwise going up", "Anticlockwise going up");
    readonly NumericUpDown _ladderW = Number(4, 100, 0.5m, "0.#"), _rungStep = Number(3, 30, 0.5m, "0.#"), _angle = Number(55, 85, 1, "0");
    readonly TextBlock _climbNote = Hint();
    Control _climbRow = null!, _stairRows = null!, _railRow = null!, _spiralRows = null!, _ladderRows = null!, _angleRow = null!, _treadRow = null!;
    Grid _riseRows = null!;

    readonly AssembledView _assembled = new();
    readonly PiecesView _pieces = new();
    readonly CheckBox _lift = new() { Content = "Lift the floor off" };
    readonly TabControl _views = new();
    readonly TabControl _tabs;
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _loading;
    int _generation;
    FloorKit? _kit;
    FloorSpec? _kitFrom;

    public StairsWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, FloorSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        // The wall maker's own textures, so a floor can use one made there.
        _own = new CustomTextures(dataFolder is null ? null : Path.Combine(Path.GetDirectoryName(dataFolder) ?? dataFolder, "walls"));
        _editing = editing;
        Title = "Floors and stairs";
        Width = 1100;
        Height = 780;
        MinWidth = 840;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        Library();

        _tabs = new TabControl
        {
            Items =
            {
                Tab("Room", RoomTab()),
                Tab("Floor", FloorTab()),
                Tab("Opening", OpeningTab()),
                Tab("Way up", WayTab()),
            },
        };
        var left = new Border { Width = 440, Padding = new Thickness(14, 10, 8, 0), Child = _tabs };

        _views.Items.Add(new TabItem { Header = "Put together", Content = new Grid { RowDefinitions = new("*,Auto"), Children = { Framed(_assembled), _lift.With(c => { Grid.SetRow(c, 1); c.Margin = new Thickness(2, 6, 0, 0); }) } } });
        _views.Items.Add(new TabItem { Header = "Pieces to cut", Content = Framed(_pieces) });
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 8, 14, 0), RowSpacing = 8 };
        right.Children.Add(_views);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 190, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(resultScroll, 1);
        right.Children.Add(resultScroll);

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        Grid.SetColumn(_make, 1);
        bottom.Children.Add(_make);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        bottom.Children.Add(close);

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto") };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        _lift.IsCheckedChanged += (_, _) => _assembled.LiftFloor = _lift.IsChecked == true;
        Hook();
        var start = editing?.Spec ?? LoadLast() ?? new FloorSpec();
        // A new floor fits the walls in the design, if there are any.
        if (editing is null && Placement.WallsIn(doc.State, doc.Selection) is { } walls) start = start.FitTo(walls);
        Load(start);
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    static Border Framed(Control c) => new() { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = c };

    // ---- building the tabs ----

    static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 10, 10), Children = { content } } },
    };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = items.Length > 0 ? 0 : -1 };

    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };

    /// <summary>Like <see cref="UiKit.Field"/>, with a label that can change.</summary>
    static Control Line(TextBlock label, Control editor, string? tip = null)
    {
        var grid = new Grid { ColumnDefinitions = new("110,*") };
        grid.Children.Add(label);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (tip is not null) { ToolTip.SetTip(label, tip); ToolTip.SetTip(editor, tip); }
        return grid;
    }

    /// <summary>A setting with its name above it (for two side by side).</summary>
    static Control Small(string label, Control editor, string? tip = null)
    {
        var l = Muted(label);
        if (tip is not null) { ToolTip.SetTip(l, tip); ToolTip.SetTip(editor, tip); }
        return Labelled(l, editor);
    }

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }

    Control RoomTab()
    {
        var match = UiKit.Button("Match the walls in the design", tip: "Reads the wall maker's settings from the walls you selected (or the first walls in the design) and fills this tab in.");
        match.HorizontalAlignment = HorizontalAlignment.Left;
        match.Click += (_, _) => MatchWalls();
        _columnRows = Stack(
            Row(Small("Column width", _colW, tip: "Across the columns' flats (mm), as in the wall maker."), Small("Column shape", _colShape)),
            _caps);
        return Stack(
            Explained("The walls", "The floor sits on top of a room of the wall maker's walls. Match the walls in the design, or type their settings in.",
                match,
                _matchNote,
                Row(Small("Wall height", _wallH, tip: "How high the walls are (mm): the floor rests on their tops."), Small("Wall sheet", _wallSheet, tip: "The sheet the walls are cut from (mm).")),
                _twoSided,
                Row(Small("Wall length", _wallLen, tip: "One wall along the room, from column centre to column centre (mm)."), Small("Walls along", _along, tip: "How many walls end to end along the room.")),
                Row(Small("Wall width", _wallWid, tip: "One wall across the room, from column centre to column centre (mm)."), Small("Walls across", _across, tip: "How many walls end to end across the room.")),
                _columns,
                _columnRows),
            Explained("Sheet and fit", "The sheet the floor and the way up are cut from. Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Row(Small("Thickness", _sheet, tip: "The sheet's thickness (mm)."), Small("Slot fit", _fit, tip: "Added to the sheet's thickness for slots (mm): more is looser.")),
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the texture (an engrave/fill recipe from your library)."),
                _cutNote));
    }

    Control FloorTab()
    {
        _sizeARow = Line(_sizeALabel, _sizeA);
        _sizeBRow = Line(_sizeBLabel, _sizeB);
        _grooveRow = Field("Groove", _groove, tip: "How wide the etched gaps between planks, stones or tiles are (mm).");
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _seedRow = Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
            tip: "Planks and stones are laid out at random: each number gives a different layout.");
        _overhangRow = Field("Overhang", _overhang, tip: "How far the floor reaches past the walls' outsides (mm): a jettied upper storey or a ledge.");
        _floorRows = Stack(
            Explained("The plate", "It rests on the walls' tops; the columns poke up through holes in it and keep it in place. The next storey's walls stand on it.",
                Field("Edge", _edge, tip: "Over the columns covers them with a little to spare; flush with the walls leaves notches round the columns; overhanging reaches further out."),
                _overhangRow,
                Field("How many", _floors, tip: "How many floors like this to cut: one for each storey."),
                Row(Small("Pieces along", _piecesAlong, tip: "Cut the floor in this many pieces along the room, joined with dovetails: for floors bigger than the bed."), Small("Pieces across", _piecesAcross, tip: "And this many across the room."))),
            Explained("Texture", "Etched on the floor's top (and on the steps): the wall maker's textures, and your own from it. Planks are the usual floor; stone blocks or tiles make flagstones.",
                Field("Texture", _texture),
                _textureAbout,
                _sizeARow, _sizeBRow, _grooveRow, _seedRow,
                Field("Etch as", _etch, tip: "Grooves etch the gaps as areas (deeper, the most real). Lines score round each plank or stone (much quicker)."),
                _across2));
        return Stack(_floor, _floorRows);
    }

    Control OpeningTab()
    {
        _hatchManual = Stack(
            Row(Small("From the corner", _hatchX, tip: "How far along the room the opening starts, from the first corner column's centre (mm)."), Small("From the side", _hatchY, tip: "How far across the room the opening starts, from the first wall's centre line (mm).")),
            Row(Small("Length", _hatchL, tip: "The opening's length along the room (mm); for spiral stairs, the round opening's diameter."), Small("Width", _hatchW, tip: "The opening's width across the room (mm).")));
        _hatchRows = Stack(_hatchAuto, _hatchManual, _trapdoor);
        return Stack(Explained("The opening", "Where the way up comes through the floor. Automatic puts it in the first corner, along the first wall, just big enough for the stairs or ladder; spiral stairs get a round one.",
            _hatch, _hatchRows));
    }

    Control WayTab()
    {
        _climbRow = Field("Climb", _climb, tip: "How high the way up climbs (mm), when there's no floor: to a wall's top, a walkway or a ledge.");
        _riseRows = Row(Small("Step height", _rise, tip: "How high each step is (mm). The climb is shared out evenly; solid and spiral stairs use whole layers of the sheet."), Small("Step depth", _going, tip: "How deep each step is, front to back (mm)."));
        _railRow = Field("Handrail", _rail, tip: "A rail on posts along the open side of the stairs, part of the side piece. Left and right as you go up.");
        _treadRow = _treadTex;
        _stairRows = Stack(Field("Width", _stairW, tip: "How wide the stairs are, between the sides (mm). 25 mm takes a figure on a 25 mm base."), _railRow);
        _spiralRows = Stack(
            Row(Small("Radius", _spiralR, tip: "From the pin to the end of each step (mm)."), Small("Turn per step °", _spiralStep, tip: "How far round each step is turned from the one under it (degrees).")),
            Row(Small("Centre Ø", _hub, tip: "The round middle of each step and the spacers between them (mm)."), Small("Pin Ø", _spiralPin, tip: "The pin they're threaded on (mm): a bamboo skewer is about 3 mm.")),
            Field("Turns", _turn));
        _angleRow = Field("Lean °", _angle, tip: "How steeply the ladder leans (degrees from the floor).");
        _ladderRows = Stack(Row(Small("Width", _ladderW, tip: "Between the rails (mm)."), Small("Rung spacing", _rungStep, tip: "Height between rungs (mm).")), _angleRow);
        return Stack(
            Explained("The way up", "Open stairs have two sides with the steps slotted through; solid stairs are stacked layers; spiral stairs are threaded on a pin. A leaning ladder is one piece; a hooked ladder hangs from the opening's edge.",
                Field("Way up", _way),
                _climbRow,
                _climbNote,
                _riseRows,
                _stairRows,
                _spiralRows,
                _ladderRows,
                _treadRow,
                Field("How many", _ways, tip: "How many like this to cut.")));
    }

    // ---- the library ----

    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Material, Recipe)?)(m, r)))];
        _etches = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Fill).Select(r => ((Material, Recipe)?)(m, r)))];
        _cutWith.ItemsSource = _cuts.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "") : "As set on the cut layer").ToList();
        _etchWith.ItemsSource = _etches.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" : "As set on the etch layer").ToList();
        _cutWith.SelectedIndex = 0;
        _etchWith.SelectedIndex = 0;
    }

    (Material M, Recipe R)? CutRecipe => _cutWith.SelectedIndex > 0 && _cutWith.SelectedIndex < _cuts.Count ? _cuts[_cutWith.SelectedIndex] : null;
    (Material M, Recipe R)? EtchRecipe => _etchWith.SelectedIndex > 0 && _etchWith.SelectedIndex < _etches.Count ? _etches[_etchWith.SelectedIndex] : null;

    void CutChosen()
    {
        if (!_loading && CutRecipe is { } c)
        {
            _loading = true;
            if (c.M.ThicknessMm is { } t) _sheet.Value = (decimal)Math.Clamp(t, 0.5, 20);
            if (c.R.SlotFitMm is { } fit) _fit.Value = (decimal)Math.Clamp(fit, -1, 2);
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so slots come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for slots at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so slots come out their drawn width."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        Changed();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _wallH, _wallSheet, _wallLen, _wallWid, _along, _across, _colW, _sheet, _fit, _overhang, _floors, _piecesAlong, _piecesAcross, _sizeA, _sizeB, _groove, _seed,
                                  _hatchX, _hatchY, _hatchL, _hatchW, _climb, _ways, _stairW, _rise, _going, _spiralR, _spiralStep, _hub, _spiralPin, _ladderW, _rungStep, _angle })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _colShape, _edge, _etch, _way, _rail, _turn, _etchWith })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _twoSided, _columns, _caps, _floor, _across2, _hatch, _hatchAuto, _trapdoor, _treadTex })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _texture.SelectionChanged += (_, _) => TextureChosen();
    }

    void MatchWalls()
    {
        var walls = Placement.WallsIn(_doc.State, _doc.Selection);
        if (walls is null) { _matchNote.Text = "There are no walls from the wall maker in the design (Home → Walls makes them)."; return; }
        var s = Current().FitTo(walls);
        Load(s);
        _matchNote.Text = $"Matched: {walls.HeightMm:0.#} mm walls of {walls.ThicknessMm:0.##} mm sheet, {s.WallLengthMm:0.#} mm between column centres." +
                          (walls.Top == TopStyle.Battlements ? " Those walls have battlements: a floor on them sits on the merlons." : "");
        Changed();
    }

    void FillTextures(string select, CustomTexture? keep)
    {
        _textureNames.Clear();
        _textureNames.AddRange(Textures.Presets.Select(p => p.Name));
        _textureNames.AddRange(_own.All.Select(t => t.Name).Where(n => !_textureNames.Contains(n)));
        if (!_textureNames.Contains(select) && keep is { } c && c.Name == select) _textureNames.Add(select);
        bool was = _loading;
        _loading = true;
        _texture.ItemsSource = _textureNames.Select(n => Textures.Preset(n) is null ? $"{n} (yours)" : n).ToList();
        _texture.SelectedIndex = Math.Max(0, _textureNames.IndexOf(select));
        _loading = was;
        ShowTextureRows();
    }

    string TextureName => _texture.SelectedIndex >= 0 && _texture.SelectedIndex < _textureNames.Count ? _textureNames[_texture.SelectedIndex] : "Wood planks (across)";

    CustomTexture? _keptCustom;

    CustomTexture? OwnTexture => Textures.Preset(TextureName) is null ? _own.Get(TextureName) ?? (_keptCustom is { } c && c.Name == TextureName ? c : null) : null;

    void TextureChosen()
    {
        if (_loading) return;
        _loading = true;
        if (Textures.Preset(TextureName) is { } p)
        {
            if (p.SizeALabel is not null) _sizeA.Value = (decimal)p.SizeA;
            if (p.SizeBLabel is not null) _sizeB.Value = (decimal)p.SizeB;
            if (p.UsesGroove) _groove.Value = (decimal)p.Groove;
        }
        else if (OwnTexture is { } own) _sizeA.Value = (decimal)Math.Clamp(own.TileWidthMm, 0.5, 300);
        _loading = false;
        ShowTextureRows();
        Changed();
    }

    void ShowTextureRows()
    {
        if (Textures.Preset(TextureName) is { } p)
        {
            _sizeALabel.Text = p.SizeALabel ?? "";
            _sizeBLabel.Text = p.SizeBLabel ?? "";
            _sizeARow.IsVisible = p.SizeALabel is not null;
            _sizeBRow.IsVisible = p.SizeBLabel is not null;
            _grooveRow.IsVisible = p.UsesGroove;
            _seedRow.IsVisible = p.Random;
            _textureAbout.Text = p.About;
        }
        else
        {
            _sizeALabel.Text = "Tile width";
            _sizeARow.IsVisible = true;
            _sizeBRow.IsVisible = _grooveRow.IsVisible = _seedRow.IsVisible = false;
            var own = OwnTexture;
            _textureAbout.Text = own is null ? "" : $"Your texture from the wall maker: a {own.TileWidthMm:0.#} × {own.TileHeightMm:0.#} mm tile, scaled to the tile width, repeated in rows.";
        }
    }

    WayUp Way => (WayUp)Math.Max(0, _way.SelectedIndex);

    void ShowRows()
    {
        bool floor = _floor.IsChecked == true, way = Way != WayUp.None;
        _floorRows.IsVisible = floor;
        _overhangRow.IsVisible = _edge.SelectedIndex == 2;
        _columnRows.IsVisible = _columns.IsChecked == true;
        _hatch.IsEnabled = floor;
        _hatchRows.IsVisible = floor && _hatch.IsChecked == true;
        _hatchManual.IsVisible = _hatchAuto.IsChecked != true || !way;
        _climbRow.IsVisible = way && !floor;
        _climbNote.IsVisible = way && floor;
        _climbNote.Text = $"It climbs to the floor's top: {V(_wallH, 50) + V(_sheet, 3):0.#} mm (the walls' height and the floor's thickness).";
        _stairRows.IsVisible = Way is WayUp.Stairs or WayUp.SolidStairs;
        _railRow.IsVisible = Way == WayUp.Stairs;
        _spiralRows.IsVisible = Way == WayUp.Spiral;
        _riseRows.IsVisible = Way is WayUp.Stairs or WayUp.SolidStairs or WayUp.Spiral;
        _ladderRows.IsVisible = Way is WayUp.Ladder or WayUp.HookedLadder;
        _angleRow.IsVisible = Way == WayUp.Ladder;
        _treadRow.IsVisible = Way is WayUp.Stairs or WayUp.SolidStairs or WayUp.Spiral;
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void Load(FloorSpec s)
    {
        _loading = true;
        _wallH.Value = D(s.WallHeightMm); _wallSheet.Value = D(s.WallSheetMm); _twoSided.IsChecked = s.TwoSidedWalls;
        _wallLen.Value = D(s.WallLengthMm); _along.Value = s.WallsAlong; _wallWid.Value = D(s.WallWidthMm); _across.Value = s.WallsAcross;
        _columns.IsChecked = s.Columns; _colShape.SelectedIndex = (int)s.Column; _colW.Value = D(s.ColumnMm); _caps.IsChecked = s.ColumnCaps;
        _sheet.Value = D(s.SheetMm); _fit.Value = D(s.FitMm);
        _floor.IsChecked = s.Floor; _edge.SelectedIndex = (int)s.Edge; _overhang.Value = D(s.OverhangMm); _floors.Value = s.Floors;
        _piecesAlong.Value = s.PiecesAlong; _piecesAcross.Value = s.PiecesAcross;
        _sizeA.Value = D(s.SizeA); _sizeB.Value = D(s.SizeB); _groove.Value = D(s.GrooveMm); _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _etch.SelectedIndex = (int)s.Etch; _across2.IsChecked = s.TextureAcross;
        _hatch.IsChecked = s.Hatch; _hatchAuto.IsChecked = s.HatchAuto; _hatchX.Value = D(s.HatchXMm); _hatchY.Value = D(s.HatchYMm);
        _hatchL.Value = D(s.HatchLengthMm); _hatchW.Value = D(s.HatchWidthMm); _trapdoor.IsChecked = s.Trapdoor;
        _way.SelectedIndex = (int)s.Way; _climb.Value = D(s.ClimbMm); _ways.Value = s.WaysUp;
        _stairW.Value = D(s.StairWidthMm); _rise.Value = D(s.RiseMm); _going.Value = D(s.GoingMm); _rail.SelectedIndex = (int)s.Rail; _treadTex.IsChecked = s.TreadTexture;
        _spiralR.Value = D(s.SpiralRadiusMm); _spiralStep.Value = D(s.SpiralStepDeg); _hub.Value = D(s.SpiralHubMm); _spiralPin.Value = D(s.SpiralPinMm); _turn.SelectedIndex = s.Clockwise ? 0 : 1;
        _ladderW.Value = D(s.LadderWidthMm); _rungStep.Value = D(s.RungStepMm); _angle.Value = D(Math.Clamp(s.LadderAngleDeg, 55, 85));
        _keptCustom = s.Custom;
        _loading = false;
        FillTextures(s.Texture, s.Custom);
        ShowRows();
        CutChosen();
    }

    FloorSpec Current() => new()
    {
        WallHeightMm = V(_wallH, 50), WallSheetMm = V(_wallSheet, 3), TwoSidedWalls = _twoSided.IsChecked == true,
        WallLengthMm = V(_wallLen, 108.5), WallsAlong = (int)V(_along, 1), WallWidthMm = V(_wallWid, 108.5), WallsAcross = (int)V(_across, 1),
        Columns = _columns.IsChecked == true, Column = (ColumnShape)Math.Max(0, _colShape.SelectedIndex), ColumnMm = V(_colW, 16), ColumnCaps = _caps.IsChecked == true,
        SheetMm = V(_sheet, 3), FitMm = V(_fit, 0.15),
        Floor = _floor.IsChecked == true, Edge = (FloorEdge)Math.Max(0, _edge.SelectedIndex), OverhangMm = V(_overhang, 6), Floors = (int)V(_floors, 1),
        PiecesAlong = (int)V(_piecesAlong, 1), PiecesAcross = (int)V(_piecesAcross, 1),
        Texture = TextureName, Custom = OwnTexture, SizeA = V(_sizeA, 5), SizeB = V(_sizeB, 35), GrooveMm = V(_groove, 0.5), Seed = (int)V(_seed, 1),
        Etch = (EtchStyle)Math.Max(0, _etch.SelectedIndex), TextureAcross = _across2.IsChecked == true,
        Hatch = _hatch.IsChecked == true, HatchAuto = _hatchAuto.IsChecked == true, HatchXMm = V(_hatchX, 15), HatchYMm = V(_hatchY, 3),
        HatchLengthMm = V(_hatchL, 60), HatchWidthMm = V(_hatchW, 34), Trapdoor = _trapdoor.IsChecked == true,
        Way = Way, ClimbMm = V(_climb, 53), WaysUp = (int)V(_ways, 1),
        StairWidthMm = V(_stairW, 25), RiseMm = V(_rise, 6), GoingMm = V(_going, 7), Rail = (Railing)Math.Max(0, _rail.SelectedIndex), TreadTexture = _treadTex.IsChecked == true,
        SpiralRadiusMm = V(_spiralR, 26), SpiralStepDeg = V(_spiralStep, 30), SpiralHubMm = V(_hub, 9), SpiralPinMm = V(_spiralPin, 3), Clockwise = _turn.SelectedIndex != 1,
        LadderWidthMm = V(_ladderW, 12), RungStepMm = V(_rungStep, 6), LadderAngleDeg = V(_angle, 75),
    };

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    FloorSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? FloorSpec.FromJson(File.ReadAllText(f)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, Current().ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- preview ----

    void Changed()
    {
        if (_loading) return;
        ShowRows();
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = Current();
        FloorKit kit;
        try { kit = await Task.Run(() => FloorKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _assembled.Show(null, spec.SheetMm, e.Message); _pieces.Show(null, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec;
        var findings = kit.Findings.Concat(Placement.TooBig(kit, _machine)).ToList();
        string none = findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to show: make a floor or a way up.";
        bool any = kit.Parts.Count > 0;
        _assembled.Show(any ? kit : null, spec.SheetMm, none);
        _pieces.Show(any ? kit : null, none);
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = !any ? "" :
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut: " + ListWords(kit.Parts.Select(p => p.Copies > 1 ? $"{p.Copies} × {Lower(p.Name)}" : Lower(p.Name))) + ".";
        _make.IsEnabled = kit.Ok && any && !findings.Any(f => f.Level == Severity.Bad);
    }

    static string Lower(string name) => name.Length > 1 && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    static Control FindingRow(Finding f)
    {
        var (mark, brush) = f.Level switch
        {
            Severity.Bad => ("✕", Brush("Danger")),
            Severity.Warn => ("!", Brush("Warn")),
            _ => ("•", Brush("TextMuted")),
        };
        var g = new Grid { ColumnDefinitions = new("20,*") };
        g.Children.Add(new TextBlock { Text = mark, Foreground = brush, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        var t = new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    // ---- putting it in ----

    bool _making;

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the pieces in the design…";
        try
        {
            var spec = Current();
            var built = _kit is not null && _kitFrom?.ToJson() == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.CountEverywhere(_doc.State, ed.Id) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? FloorKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, spec, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Floor and stairs" : "Update the floor and stairs", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{Placement.CutLayer}\", the texture on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("stairs", ex);
            _status.Text = $"Couldn't put the pieces in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
