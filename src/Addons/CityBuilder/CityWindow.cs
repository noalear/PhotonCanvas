using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// The city builder: a town on a big grid, seen from above (the plan) and in a light 3D view. Paint the
/// ground, drag out buildings or put down saved ones, draw fences and walls, drop trees; or read a battle map
/// picture for a best guess of the whole town and put right what it got wrong. Each building opens in the
/// building editor, can be saved there for use again, and comes out as its laser-cut pieces.
/// </summary>
public sealed partial class CityWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    CityMap _map;
    string? _file;
    readonly Stack<(CityMap Map, string Label)> _undo = new(), _redo = new();

    // The map
    readonly TextBox _name = new();
    readonly ComboBox _theme = Combo([.. BuildingThemes.Names]);
    readonly NumericUpDown _across = Number(1, CityMap.MaxSide, 1, "0"), _down = Number(1, CityMap.MaxSide, 1, "0"), _square = Number(10, 150, 0.5m, "0.##");
    readonly TextBlock _mapInfo = Hint();
    readonly Button _resize = UiKit.Button("Make it this size"), _newMap = UiKit.Button("New map"), _openMap = UiKit.Button("Open…"), _saveMap = UiKit.Button("Save as…"), _sample = UiKit.Button("Sample village");

    // Making a town
    readonly NumericUpDown _tAcross = Number(8, CityMap.MaxSide, 4, "0"), _tDown = Number(8, CityMap.MaxSide, 4, "0");
    readonly ComboBox _tLayout = Combo("Crossroads", "Main street", "Grid of streets", "Winding road", "Riverside");
    readonly Slider _tDensity = new() { Minimum = 0, Maximum = 1, Value = 0.6 };
    readonly CheckBox _tWalls = new() { Content = "A town wall with gates" }, _tRiver = new() { Content = "A river through it" },
        _tSquare = new() { Content = "A market square", IsChecked = true }, _tSaved = new() { Content = "Use my saved buildings where they suit", IsChecked = true };
    readonly Button _tMake = new() { Content = "Make a town", Classes = { "primary" } };
    readonly ComboBox _fDistrict = Combo([.. HousePrefabs.Districts.Where(d => d != "Anywhere")]);
    readonly Button _fill = UiKit.Button("Fill with buildings");
    readonly TextBlock _tNotes = Hint();
    int _seed = Environment.TickCount & 0xFFFF;

    // Saving a picture
    readonly NumericUpDown _pPx = Number(20, 300, 10, "0");
    readonly CheckBox _pGrid = new() { Content = "The grid", IsChecked = true }, _pLabels = new() { Content = "Street names and labels", IsChecked = true },
        _pNames = new() { Content = "Building names" }, _pBox = new() { Content = "Only the last box dragged with Select" };
    readonly Button _pSave = UiKit.Button("Save as a picture…");
    readonly TextBlock _pInfo = Hint();

    // Cutting the ground, bridges and scenery
    readonly NumericUpDown _cTile = Number(1, 10, 1, "0"), _cThick = Number(1, 10, 0.5m, "0.##");
    readonly ComboBox _cJoin = Combo("Jigsaw edges", "Clips", "Magnets", "Plain edges");
    readonly CheckBox _cGrid = new() { Content = "Score the grid", IsChecked = true }, _cOutlines = new() { Content = "Score where the buildings stand", IsChecked = true },
        _cBox = new() { Content = "Only the last box dragged with Select" };
    readonly Button _cGround = UiKit.Button("Put the ground in the design"), _cBridges = UiKit.Button("Put the bridges in the design"), _cScenery = UiKit.Button("Put the scenery in the design");
    readonly TextBlock _cGroundInfo = Hint(), _cBridgesInfo = Hint(), _cSceneryInfo = Hint(), _cWallsInfo = Hint(), _cTreesInfo = Hint();
    readonly Button _cWalls = UiKit.Button("Put the town walls in the design"), _cTrees = UiKit.Button("Put the trees in the design");
    readonly Button _cFurniture = UiKit.Button("Put the furniture in the design");
    readonly TextBlock _cFurnitureInfo = Hint();

    // Battle map
    readonly Button _openPic = UiKit.Button("Open a picture…"), _usePic = UiKit.Button("Use the selected picture"), _samplePic = UiKit.Button("Try a sample battle map"), _findGrid = UiKit.Button("Find the grid");
    readonly Button _read = new() { Content = "Read the map", Classes = { "primary" } };
    readonly NumericUpDown _px = Number(4, 2000, 0.5m, "0.##"), _offX = Number(-2000, 2000, 1, "0.#"), _offY = Number(-2000, 2000, 1, "0.#"), _picAcross = Number(1, CityMap.MaxSide, 1, "0");
    readonly ComboBox _shows = Combo("Worked out from the picture", "Seen from above (roofs)", "Drawn as floor plans (walls in lines)");
    readonly CheckBox _findBuildings = new() { Content = "Find buildings", IsChecked = true }, _findTrees = new() { Content = "Find trees", IsChecked = true }, _usePrefabs = new() { Content = "Use my saved buildings where one fits", IsChecked = true };
    readonly Slider _sensitivity = new() { Minimum = 0, Maximum = 1, Value = 0.5 }, _opacity = new() { Minimum = 0, Maximum = 1, Value = 0.45 };
    readonly NumericUpDown _largest = Number(4, 2000, 4, "0");
    readonly CheckBox _showPic = new() { Content = "Show the picture under the plan", IsChecked = true };
    readonly TextBlock _picNote = Hint(), _readNotes = Hint();
    RasterImage? _picture;

    // Tools
    readonly Dictionary<CityTool, ToggleButton> _tools = [];
    readonly NumericUpDown _brush = Number(1, 15, 1, "0");
    readonly ComboBox _typeFilter = Combo("Every type");
    readonly ListBox _prefabList = new() { Height = 220 };
    readonly Button _refreshPrefabs = UiKit.Button("Look again");
    readonly TextBlock _prefabNote = Hint();
    readonly NumericUpDown _storeysNew = Number(1, HouseKitMaker.MaxLevels, 1, "0");
    readonly NumericUpDown _roadWidth = Number(1, 9, 1, "0");
    readonly TextBox _labelText = new() { Text = "Market Street" };
    readonly NumericUpDown _labelSize = Number(0.2m, 10, 0.1m, "0.#"), _labelTurn = Number(-90, 90, 15, "0");
    readonly CheckBox _fenceRuined = new() { Content = "Ruined (broken, its ends left open)" };
    List<PrefabInfo> _prefabs = [];

    /// <summary>What the Road tool can lay.</summary>
    static readonly Ground[] RoadGrounds = [Ground.Road, Ground.Dirt, Ground.Paving, Ground.Sand];

    // The town's buildings
    readonly TextBox _find = new() { Watermark = "Find by name, type, district or tag" };
    readonly ComboBox _findType = Combo("Every type"), _findDistrict = Combo("Every district");
    readonly ListBox _townList = new() { Height = 300 };
    readonly TextBlock _townCounts = Hint();
    readonly CheckBox _byDistrict = new() { Content = "Colour the plan by district" };
    readonly StackPanel _legend = new() { Spacing = 3 };
    bool _fromList;

    // Several things picked, or a label
    readonly TextBlock _gInfo = Hint();
    readonly TextBox _gType = new() { Watermark = "Leave empty to keep each one's" }, _gDistrict = new() { Watermark = "Leave empty to keep each one's" };
    readonly ComboBox _gProp = Combo([.. Enum.GetValues<PropKind>().Select(CityMap.Label)]);
    readonly NumericUpDown _gPropSize = Number(0.2m, 6, 0.1m, "0.#");
    readonly Button _gTurn = UiKit.Button("Turn ⟳"), _gCopy = UiKit.Button("Copy beside it"), _gDelete = UiKit.Button("Take away");
    readonly Button _gMake = UiKit.Button("Put the picked buildings in the design");
    Control _groupBuildings = null!, _groupProps = null!, _groupPanel = null!, _labelPanel = null!;
    readonly TextBox _lText = new();
    readonly NumericUpDown _lSize = Number(0.2m, 10, 0.1m, "0.#"), _lTurn = Number(-90, 90, 15, "0");
    readonly Button _lDelete = UiKit.Button("Take it away");

    // A piece of furniture picked
    readonly TextBox _iName = new();
    readonly NumericUpDown _iTurn = Number(0, 359, 15, "0");
    readonly TextBlock _iInfo = Hint();
    readonly Button _iCopy = UiKit.Button("Copy beside it"), _iDelete = UiKit.Button("Take it away");
    Control _itemPanel = null!;

    // The Furniture tool's pieces
    readonly ComboBox _fuRoom = Combo([.. CityItems.Rooms]), _fuLook = Combo([.. PhotonCanvas.Addon.Furniture.FurnitureCatalog.Themes]);
    readonly NumericUpDown _fuTurn = Number(0, 359, 15, "0");
    readonly WrapPanel _fuPieces = new();
    readonly List<(PhotonCanvas.Addon.Furniture.CatalogEntry Entry, ToggleButton Button)> _fuButtons = [];
    string? _fuLookFor;
    int _fuVersion;
    /// <summary>The furniture palette's pictures being drawn (for the help's pictures to wait for).</summary>
    Task _fuPictures = Task.CompletedTask;

    // A road or a wall picked
    readonly TextBox _rName = new();
    readonly ComboBox _rSurface = Combo([.. RoadGrounds.Select(CityMap.Label)]);
    readonly NumericUpDown _rWidth = Number(1, 9, 1, "0");
    readonly TextBlock _rInfo = Hint();
    readonly Button _rDelete = UiKit.Button("Take it away");
    readonly ComboBox _wKind = Combo([.. Enum.GetValues<FenceKind>().Select(CityMap.Label)]);
    readonly CheckBox _wRuined = new() { Content = "Ruined (broken, its ends left open)" };
    readonly TextBlock _wInfo = Hint();
    readonly Button _wDelete = UiKit.Button("Take it away");
    Control _roadPanel = null!, _fencePanel = null!;

    // The building picked
    readonly TextBox _bName = new(), _bType = new(), _bDistrict = new(), _bTags = new();
    readonly ComboBox _bCovering = Combo([.. Roofs.Shingles.Presets.Select(p => p.Name)]);
    readonly ComboBox _bRoof = Combo("Gable", "Hipped", "Lean-to", "Flat");
    readonly Button _bTurn = UiKit.Button("Turn ⟳"), _bUp = UiKit.Button("Add a floor"), _bDownLevel = UiKit.Button("Take the top floor off"), _bEdit = new() { Content = "Edit in the building editor…", Classes = { "primary" } };
    readonly Button _bCopy = UiKit.Button("Copy it"), _bDelete = UiKit.Button("Take it away"), _bRoofIt = UiKit.Button("Roof it again"), _bSavePrefab = UiKit.Button("Save as a building to reuse");
    readonly TextBox _bTilt = new();
    readonly Button _bSquare = UiKit.Button("Square it to the grid");
    readonly TextBlock _bInfo = Hint(), _bBuilt = Hint();
    Control _buildingPanel = null!, _noBuilding = null!;
    bool _loadingBuilding;

    // Views
    readonly CityPlan _plan = new();
    readonly CityView _view = new();
    readonly CheckBox _vRoofs = new() { Content = "Roofs", IsChecked = true }, _vProps = new() { Content = "Trees and things", IsChecked = true }, _vOpenings = new() { Content = "Doors and windows", IsChecked = true },
        _vBuilt = new() { Content = "As built", IsChecked = true }, _vDoors = new() { Content = "Doors open" };
    /// <summary>How far the buildings are made as built, in the 3D view's corner while they're being made.</summary>
    readonly TextBlock _vMaking = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    /// <summary>Looks in while the buildings are made as built (in the background), to show each as it's done.</summary>
    readonly DispatcherTimer _built = new() { Interval = TimeSpan.FromMilliseconds(400) };
    int _builtSeen = -1, _shownBuilt = -1;
    DateTime _built3D;
    readonly Button _undoButton = UiKit.Button("Undo"), _redoButton = UiKit.Button("Redo");
    readonly TextBlock _summary = new();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _makeOne = UiKit.Button("Put this building in the design");
    readonly Button _makeAll = UiKit.Button("Put every building in the design");
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromSeconds(2) };
    bool _viewStale = true, _loading;

    public CityWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, CityMap? map = null)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _map = map ?? LoadLast() ?? Sample();
        Title = "City builder";
        Width = 1360;
        Height = 880;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Content = BuildLayout();
        ContextHelp.Off(_plan);
        _plan.PickChanged += () =>
        {
            // Something picked on the plan shows on the Picked tab (unless one of the toolbar's pages is open, which may want the box just dragged).
            if (!_plan.Pick.IsEmpty && !_fromList && _plan.Tool is CityTool.Select or CityTool.Label or CityTool.Furnish && (_side.SelectedItem == _townTab || _side.SelectedItem == _mapTab)) _side.SelectedItem = _pickedTab;
            ShowPicked();
            RefreshPictureInfo();
            if (OnPage(_cutPage)) RefreshCut();
        };

        Wire();
        LoadPrefabs();
        PickTool(CityTool.Select);
        Load(_map, refit: true);
        _save.Tick += (_, _) => { _save.Stop(); SaveLast(); };
        _built.Tick += (_, _) => BuiltTick();
        Closed += (_, _) => { SaveLast(); _built.Stop(); };
        KeyDown += OnKey;
        _tAcross.Value = _map.Across;
        _tDown.Value = _map.Down;
        FillFurniture();
        SyncPalettes();
        ShowView(false);
        Opened += async (_, _) => await LoadPictures();
    }

    // ---- wiring ----

    void Wire()
    {
        _plan.Edited += (m, label) => Commit(m, label, fromPlan: true);
        _plan.NewBuilding = (x, y, w, d) =>
        {
            var cells = new HashSet<(int X, int Y)>();
            for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) cells.Add((i, j));
            var (type, _) = BuildingGuess.Kind(cells);
            var paved = _map.Paved();
            var towards = Around(cells).Where(c => paved.GroundAt(c.X, c.Y) is Ground.Road or Ground.Paving or Ground.Dirt).ToHashSet();
            var f = new SeenBuilding { Cells = cells, Storeys = (int)(_storeysNew.Value ?? 1), Type = type, DoorTowards = towards };
            if (BuildingGuess.Make(f, _map.Theme, _map.SquareMm) is not { } made) return null;
            return new CityBuilding { Name = TownMaker.BuildingName(_map, type, Environment.TickCount), Type = type, X = made.X, Y = made.Y, Design = made.Design };
        };
        _plan.NameRoad = m => TownMaker.RoadName(m, Environment.TickCount);
        _plan.OpenBuilding += async id => { if (_map.Building(id) is not null) { _plan.Select(id); await EditBuilding(); } };
        _plan.Said += text => _status.Text = text;
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();

        _name.LostFocus += (_, _) => { if (!_loading && _name.Text != _map.Name) Commit(_map with { Name = string.IsNullOrWhiteSpace(_name.Text) ? "Town" : _name.Text.Trim() }, "Rename the map"); };
        _theme.SelectionChanged += (_, _) => { if (!_loading && _theme.SelectedItem is string t && t != _map.Theme) Commit(_map with { Theme = t }, "Change the look"); };
        _square.ValueChanged += (_, _) => { if (!_loading && _square.Value is { } v && (double)v != _map.SquareMm) Commit(_map with { SquareMm = (double)v }, "Change the square"); };
        _resize.Click += (_, _) => Commit(_map.Resized((int)(_across.Value ?? 40), (int)(_down.Value ?? 30)), "Change the map's size", refit: true);
        _newMap.Click += (_, _) => { _file = null; _picture = null; _plan.SetPicture(null, 0, 0); Commit(CityMap.Empty((int)(_across.Value ?? 40), (int)(_down.Value ?? 30)) with { Theme = _map.Theme, SquareMm = _map.SquareMm }, "New map", refit: true); };
        _sample.Click += (_, _) => { _file = null; Commit(Sample(), "Sample village", refit: true); };
        _openMap.Click += async (_, _) => await OpenMap();
        _saveMap.Click += async (_, _) => await SaveMapAs();

        _pPx.Value = 100;
        _pSave.Click += async (_, _) => await SavePicture();
        foreach (var n in new[] { _pPx }) n.ValueChanged += (_, _) => RefreshPictureInfo();
        _pBox.IsCheckedChanged += (_, _) => RefreshPictureInfo();
        _cTile.Value = 4;
        _cThick.Value = 3;
        _cBox.IsCheckedChanged += (_, _) => RefreshCut();
        _cTile.ValueChanged += (_, _) => RefreshCut();
        _cGround.Click += async (_, _) => await CutGround();
        _cBridges.Click += async (_, _) => await CutBridges();
        _cScenery.Click += async (_, _) => await CutScenery();
        _cWalls.Click += async (_, _) => await CutFurniture(_cWalls, TownPieces.WallSet, "City walls", "the town walls");
        _cTrees.Click += async (_, _) => await CutFurniture(_cTrees, TownPieces.TreeSet, "City trees", "the trees");
        _cFurniture.Click += async (_, _) => await CutFurniture(_cFurniture, CityItems.Set, "City furniture", "the furniture");

        _tMake.Click += async (_, _) => await MakeTown();
        _fill.Click += async (_, _) => await FillWithBuildings();
        _fDistrict.SelectedItem = "Residential";

        _openPic.Click += async (_, _) => await OpenPicture();
        _usePic.Click += (_, _) => UseSelectedPicture();
        _samplePic.Click += async (_, _) => await UsePicture(SampleBattleMap.Make(), null);
        _findGrid.Click += async (_, _) => { if (_picture is { } img) await FindGrid(img); };
        _read.Click += async (_, _) => await ReadPicture();
        _showPic.IsCheckedChanged += (_, _) => _plan.ShowPicture = _showPic.IsChecked == true;
        _opacity.ValueChanged += (_, _) => _plan.GroundOpacity = _opacity.Value;
        _plan.GroundOpacity = _opacity.Value;
        _largest.Value = 120;
        _picAcross.ValueChanged += (_, _) =>
        {
            if (_loading || _picture is null || _picAcross.Value is not { } n || n < 1) return;
            _loading = true;
            _px.Value = (decimal)Math.Round(_picture.Width / (double)n, 2);
            _offX.Value = 0;
            _offY.Value = 0;
            _loading = false;
        };

        _brush.ValueChanged += (_, _) => _plan.Brush = (int)(_brush.Value ?? 1);
        _brush.Value = 1;
        _storeysNew.Value = 1;
        _roadWidth.ValueChanged += (_, _) => { _plan.RoadWidth = (int)(_roadWidth.Value ?? 2); SyncPalettes(); };
        _roadWidth.Value = 2;
        _labelText.TextChanged += (_, _) => { _plan.LabelText = _labelText.Text ?? ""; _plan.InvalidateVisual(); };
        _labelSize.ValueChanged += (_, _) => _plan.LabelSize = (double)(_labelSize.Value ?? 0.8m);
        _labelSize.Value = 0.8m;
        _labelTurn.ValueChanged += (_, _) => { _plan.LabelTurn = (double)(_labelTurn.Value ?? 0); _plan.InvalidateVisual(); };
        _labelTurn.Value = 0;
        _fenceRuined.IsCheckedChanged += (_, _) => _plan.FenceRuined = _fenceRuined.IsChecked == true;
        _plan.LabelTurnChanged += t => { _loading = true; _labelTurn.Value = (decimal)Math.Round(t); _loading = false; };
        _fuRoom.SelectionChanged += (_, _) => FillFurniture();
        _fuLook.SelectionChanged += (_, _) => FillFurniture();
        _fuTurn.ValueChanged += (_, _) => { if (_fuSyncing) return; _plan.FurnishTurn = (double)(_fuTurn.Value ?? 0); _plan.InvalidateVisual(); };
        _plan.FurnishTurnChanged += _ => SyncFurniture();
        _iName.LostFocus += (_, _) => ChangeItem(i => i with { Name = string.IsNullOrWhiteSpace(_iName.Text) ? i.Name : _iName.Text.Trim() }, "Rename furniture");
        _iTurn.ValueChanged += (_, _) => ChangeItem(i => i with { TurnDeg = (double)(_iTurn.Value ?? 0) % 360 }, "Turn furniture");
        _iCopy.Click += (_, _) => _plan.DuplicatePicked();
        _iDelete.Click += (_, _) => _plan.DeletePicked();

        _find.TextChanged += (_, _) => FillTownList();
        _findType.SelectionChanged += (_, _) => FillTownList();
        _findDistrict.SelectionChanged += (_, _) => FillTownList();
        _townList.SelectionChanged += (_, _) =>
        {
            if (_townList.SelectedItem is not TownRow r || _map.Building(r.Building.Id) is not { } b || _plan.Selected == b.Id) return;
            _fromList = true;
            _plan.SetPick(CityPick.Of(b));
            _fromList = false;
            var (cx, cy) = CityFootprint.Centre(b);
            _plan.CentreOn(cx, cy);
            ShowView(false);
        };
        _byDistrict.IsCheckedChanged += (_, _) => _plan.DistrictColours = _byDistrict.IsChecked == true;

        _gType.LostFocus += (_, _) => ChangePicked(b => b with { Type = _gType.Text!.Trim() }, string.IsNullOrWhiteSpace(_gType.Text), "Change the buildings' type");
        _gDistrict.LostFocus += (_, _) => ChangePicked(b => b with { District = _gDistrict.Text!.Trim() }, string.IsNullOrWhiteSpace(_gDistrict.Text), "Change the buildings' district");
        _gProp.SelectionChanged += (_, _) => ChangeProps(p => p with { Kind = (PropKind)Math.Max(0, _gProp.SelectedIndex) }, "Change trees and things");
        _gPropSize.ValueChanged += (_, _) => ChangeProps(p => p with { Size = (double)(_gPropSize.Value ?? 1) }, "Resize trees and things");
        _gTurn.Click += (_, _) => _plan.TurnPicked(1);
        _gCopy.Click += (_, _) => _plan.DuplicatePicked();
        _gDelete.Click += (_, _) => _plan.DeletePicked();
        _gMake.Click += async (_, _) => await Make([.. _map.Buildings.Where(b => _plan.Pick.Buildings.Contains(b.Id))]);
        _lText.LostFocus += (_, _) => ChangeLabel(l => l with { Text = string.IsNullOrWhiteSpace(_lText.Text) ? l.Text : _lText.Text.Trim() }, "Change a label");
        _lSize.ValueChanged += (_, _) => ChangeLabel(l => l with { Size = (double)(_lSize.Value ?? 0.8m) }, "Resize a label");
        _lTurn.ValueChanged += (_, _) => ChangeLabel(l => l with { TurnDeg = CityLabel.Upright((double)(_lTurn.Value ?? 0)) }, "Turn a label");
        _lDelete.Click += (_, _) => _plan.DeletePicked();
        _rName.LostFocus += (_, _) => ChangeRoad(r => r with { Name = (_rName.Text ?? "").Trim() }, "Rename a road");
        _rSurface.SelectionChanged += (_, _) => ChangeRoad(r => r with { Surface = RoadGrounds[Math.Clamp(_rSurface.SelectedIndex, 0, RoadGrounds.Length - 1)] }, "Change a road's surface");
        _rWidth.ValueChanged += (_, _) => ChangeRoad(r => r with { Width = (double)(_rWidth.Value ?? 2) }, "Change a road's width");
        _rDelete.Click += (_, _) => _plan.DeletePicked();
        _wKind.SelectionChanged += (_, _) => ChangeFence(f => f with { Kind = (FenceKind)Math.Max(0, _wKind.SelectedIndex) }, "Change a wall's kind");
        _wRuined.IsCheckedChanged += (_, _) => ChangeFence(f => f with { Ruined = _wRuined.IsChecked == true }, _wRuined.IsChecked == true ? "Ruin a wall" : "Mend a wall");
        _wDelete.Click += (_, _) => _plan.DeletePicked();
        _typeFilter.SelectionChanged += (_, _) => FillPrefabList();
        _prefabList.SelectionChanged += (_, _) =>
        {
            _plan.ToPlace = _prefabList.SelectedItem is PrefabRow r ? r.Prefab : null;
            if (_plan.ToPlace is not null) PickTool(CityTool.Place);
        };
        _refreshPrefabs.Click += (_, _) => LoadPrefabs();

        _bName.LostFocus += (_, _) => ChangeBuilding(b => b with { Name = string.IsNullOrWhiteSpace(_bName.Text) ? b.Name : _bName.Text.Trim() }, "Rename a building");
        _bType.LostFocus += (_, _) => ChangeBuilding(b => b with { Type = (_bType.Text ?? "").Trim() }, "Change a building's type");
        _bDistrict.LostFocus += (_, _) => ChangeBuilding(b => b with { District = (_bDistrict.Text ?? "").Trim() }, "Change a building's district");
        _bTags.LostFocus += (_, _) => ChangeBuilding(b => b with { Tags = Tags(_bTags.Text) }, "Change a building's tags");
        _bTurn.Click += (_, _) => _plan.TurnPicked(1);
        _bTilt.LostFocus += (_, _) => ChangeBuilding(b => double.TryParse(_bTilt.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double t) ? b with { TiltDeg = Math.Clamp(t, -89, 89) } : b, "Tilt a building");
        _bSquare.Click += (_, _) => ChangeBuilding(b => b with { TiltDeg = 0, ShiftX = 0, ShiftY = 0, X = (int)Math.Round(b.X + b.ShiftX), Y = (int)Math.Round(b.Y + b.ShiftY) }, "Square a building to the grid");
        _bUp.Click += (_, _) => ChangeBuilding(b => b with { Design = AddFloor(b.Design) }, "Add a floor");
        _bDownLevel.Click += (_, _) => ChangeBuilding(b => b with { Design = TakeFloor(b.Design) }, "Take a floor off");
        _bRoof.SelectionChanged += (_, _) => ChangeBuilding(b => b with { Design = b.Design with { Roofs = [.. b.Design.Roofs.Select(r => r with { Kind = (RoofKind)Math.Max(0, _bRoof.SelectedIndex) })] } }, "Change a roof");
        _bCovering.SelectionChanged += (_, _) => ChangeBuilding(b => b with { Design = b.Design with { Roofs = [.. b.Design.Roofs.Select(r => r with { Covering = _bCovering.SelectedItem as string ?? r.Covering })] } }, "Change a roof's covering");
        _bRoofIt.Click += (_, _) => ChangeBuilding(b => b with { Design = RoofAgain(b.Design) }, "Roof a building again");
        _bEdit.Click += async (_, _) => await EditBuilding();
        _bCopy.Click += (_, _) => CopyBuilding();
        _bDelete.Click += (_, _) => _plan.DeletePicked();
        _bSavePrefab.Click += async (_, _) => await SaveAsPrefab();

        foreach (var c in new[] { _vRoofs, _vProps, _vOpenings, _vBuilt, _vDoors }) c.IsCheckedChanged += (_, _) => Show3D();
        _vBuilt.IsCheckedChanged += (_, _) => { _vDoors.IsEnabled = _vBuilt.IsChecked == true; ShowMaking(); };
        _makeOne.Click += async (_, _) => await Make([.. _map.Buildings.Where(b => _plan.Pick.Buildings.Contains(b.Id))]);
        _makeAll.Click += async (_, _) => await Make(_map.Buildings);
    }

    static IEnumerable<(int X, int Y)> Around(HashSet<(int X, int Y)> cells)
    {
        foreach (var (x, y) in cells)
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (!cells.Contains(n)) yield return n;
    }

    static List<string> Tags(string? text) => [.. (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) { Redo(); e.Handled = true; }
        // The number keys pick the tools, in the order they're down the left (not while typing).
        else if (e.KeyModifiers == KeyModifiers.None && ToolForKey(e.Key) is { } t && FocusManager?.GetFocusedElement() is not (TextBox or ComboBox or NumericUpDown))
        {
            PickTool(t);
            e.Handled = true;
        }
    }

    void PickTool(CityTool t)
    {
        _plan.Tool = t;
        foreach (var (k, b) in _tools) b.IsChecked = k == t;
        if (_paletteItems.TryGetValue(t, out var item) && _palette.SelectedItem != item) _palette.SelectedItem = item;
        if (t == CityTool.Place && _plan.ToPlace is null) _status.Text = "Pick a saved building beside the tools first.";
        if (t == CityTool.Furnish && _plan.ToFurnish is null) FillFurniture();
        _plan.InvalidateVisual();
    }

    // ---- the map and undo ----

    void Commit(CityMap m, string label, bool fromPlan = false, bool refit = false)
    {
        if (ReferenceEquals(m, _map) && !fromPlan) return;
        _undo.Push((_map, label));
        _redo.Clear();
        _map = m;
        if (!fromPlan) _plan.Show(m, refit);
        Refresh();
        _status.Text = label;
        _save.Stop();
        _save.Start();
    }

    void Undo()
    {
        if (_undo.Count == 0) return;
        var (m, label) = _undo.Pop();
        _redo.Push((_map, label));
        _map = m;
        _plan.Show(m);
        Refresh();
        _status.Text = $"Undone: {label}";
    }

    void Redo()
    {
        if (_redo.Count == 0) return;
        var (m, label) = _redo.Pop();
        _undo.Push((_map, label));
        _map = m;
        _plan.Show(m);
        Refresh();
        _status.Text = $"Done again: {label}";
    }

    void Load(CityMap m, bool refit = false)
    {
        _map = m;
        _plan.Show(m, refit);
        Refresh();
    }

    void Refresh()
    {
        _loading = true;
        _name.Text = _map.Name;
        _theme.SelectedItem = BuildingThemes.Names.Contains(_map.Theme) ? _map.Theme : BuildingThemes.Names[0];
        _across.Value = _map.Across;
        _down.Value = _map.Down;
        _square.Value = (decimal)_map.SquareMm;
        _loading = false;
        // The furniture's look follows the map's when that changes.
        if (_fuLookFor != _map.Theme)
        {
            _fuLookFor = _map.Theme;
            _fuLook.SelectedItem = PhotonCanvas.Addon.Furniture.FurnitureCatalog.Themes.Contains(_map.Theme) ? _map.Theme : "Medieval";
        }
        _undoButton.IsEnabled = _undo.Count > 0;
        _redoButton.IsEnabled = _redo.Count > 0;
        Summarise();
        _makeAll.IsEnabled = _map.Buildings.Count > 0;
        RefreshTown();
        ShowPicked();
        RefreshPictureInfo();
        if (OnPage(_cutPage)) RefreshCut();
        _viewStale = true;
        if (_threeD) Show3D();
        // Every building made as built in the background, for the 3D view and to see where they'd run into each other.
        if (CityBuilt.WarmUp(_map) > 0 && !_built.IsEnabled) _built.Start();
        ShowMaking();
    }

    /// <summary>The map's size and what's on it, and anything wrong: buildings on the same squares, roads through them, buildings that'd run into each other once built.</summary>
    void Summarise()
    {
        var overlaps = CityFootprint.Overlaps(_map);
        double wM = _map.Across * _map.SquareMm / 1000, dM = _map.Down * _map.SquareMm / 1000;
        var cells = CityEdit.BuildingCells(_map);
        int onRoads = _map.Roads.Count(r => CityEdit.Hits(_map, r, cells));
        int intrude = CityBuilt.Intrusions(_map).Count(x => !overlaps.Any(o => o.A.Id == x.A.Id && o.B.Id == x.B.Id || o.A.Id == x.B.Id && o.B.Id == x.A.Id));
        string warnings = (overlaps.Count > 0 ? $" {overlaps.Count} pair{(overlaps.Count == 1 ? "" : "s")} of buildings stand on the same squares (red on the plan)." : "")
            + (onRoads > 0 ? $" {onRoads} road{(onRoads == 1 ? " runs" : "s run")} through a building." : "")
            + (intrude > 0 ? $" {intrude} pair{(intrude == 1 ? "" : "s")} of buildings would run into each other once built (their roofs or what sticks out of their walls; red on the plan)." : "");
        _mapInfo.Text = $"{_map.Name}: {_map.Across} × {_map.Down} squares ({wM:0.##} × {dM:0.##} m on the table), {_map.Buildings.Count} building{(_map.Buildings.Count == 1 ? "" : "s")}, {_map.Roads.Count} road{(_map.Roads.Count == 1 ? "" : "s")}, {_map.Props.Count} trees and things" + (_map.Items.Count > 0 ? $", {_map.Items.Count} piece{(_map.Items.Count == 1 ? "" : "s")} of furniture." : ".") + warnings;
        _summary.Text = $"{_map.Across} × {_map.Down} squares ({wM:0.##} × {dM:0.##} m) · {_map.Buildings.Count} building{(_map.Buildings.Count == 1 ? "" : "s")} · {_map.Roads.Count} road{(_map.Roads.Count == 1 ? "" : "s")} · {_map.Props.Count} trees and things" + (_map.Items.Count > 0 ? $" · {_map.Items.Count} furniture" : "") + (warnings.Length > 0 ? " ·" + warnings : "");
    }

    /// <summary>
    /// Another building made as built: the plan shows where it reaches and the warnings are looked at again; the
    /// 3D view shows it (every second and a half or so while they're being made, so a big town doesn't hold it up).
    /// </summary>
    void BuiltTick()
    {
        int left = CityBuilt.WarmUp(_map);
        if (left == 0) _built.Stop();
        ShowMaking();
        if (CityBuilt.Version != _builtSeen)
        {
            _builtSeen = CityBuilt.Version;
            _plan.InvalidateVisual();
            Summarise();
            if (Picked() is { } b) _bBuilt.Text = BuiltText(b);
        }
        if (_vBuilt.IsChecked != true || _shownBuilt == CityBuilt.Version) return;
        if (!_threeD) _viewStale = true;
        else if (left == 0 || DateTime.UtcNow - _built3D > TimeSpan.FromSeconds(1.5)) Show3D();
    }

    void ShowMaking()
    {
        var (done, of) = CityBuilt.Progress(_map);
        _vMaking.IsVisible = _vBuilt.IsChecked == true && done < of;
        _vMaking.Text = $"Making them as built: {done} of {of}…";
    }

    /// <summary>What a building covers once it's built, with its roof and all that sticks out of its walls.</summary>
    static string BuiltText(CityBuilding b)
    {
        if (CityBuilt.Known(b.Design) is not { } m) return "Working out its size as built…";
        if (!m.Ok) return "It can't be made as it is: open it in the building editor to see why.";
        var box = m.Box;
        double w = box.right - box.left, d = box.bottom - box.top;
        if ((b.Turn & 1) == 1) (w, d) = (d, w);
        var (pw, pd) = CityFootprint.Size(b);
        double past = Math.Max(Math.Max(-box.left, -box.top), Math.Max(box.right - CityFootprint.Extent(b.Design).W, box.bottom - CityFootprint.Extent(b.Design).D));
        return $"As built, with its roof and what sticks out of its walls, it covers {w:0.#} × {d:0.#} squares ({w * m.SquareMm:0} × {d * m.SquareMm:0} mm), "
            + $"reaching {(past < 0.95 ? $"{past:0.#} of a square" : $"{past:0.#} squares")} past its {pw} × {pd} (dashed round it on the plan). Keep {Math.Max(1, Math.Ceiling(past * 2)):0} squares between it and the next like it, so they don't touch.";
    }

    void Show3D()
    {
        _viewStale = false;
        _built3D = DateTime.UtcNow;
        _shownBuilt = CityBuilt.Version;
        _view.Show(_map, new CityShow
        {
            Roofs = _vRoofs.IsChecked == true, Props = _vProps.IsChecked == true, Openings = _vOpenings.IsChecked == true,
            AsBuilt = _vBuilt.IsChecked == true, DoorsOpen = _vDoors.IsChecked == true,
            Picked = [.. _plan.Pick.Buildings, .. _plan.Pick.Items],
        });
    }

    // ---- the building picked ----

    CityBuilding? Picked() => _plan.Selected is { } id ? _map.Building(id) : null;

    void ShowPicked()
    {
        var pick = _plan.Pick;
        var b = Picked();
        var label = pick.Labels.Count == 1 && pick.Count == 1 ? _map.Labels.FirstOrDefault(l => l.Id == pick.Labels.First()) : null;
        var road = pick.Roads.Count == 1 && pick.Count == 1 ? _map.Road(pick.Roads.First()) : null;
        var fence = pick.Fences.Count == 1 && pick.Count == 1 ? _map.FenceById(pick.Fences.First()) : null;
        var item = pick.Items.Count == 1 && pick.Count == 1 ? _map.ItemById(pick.Items.First()) : null;
        _noBuilding.IsVisible = pick.IsEmpty;
        _itemPanel.IsVisible = item is not null;
        _buildingPanel.IsVisible = b is not null;
        _labelPanel.IsVisible = label is not null;
        _roadPanel.IsVisible = road is not null;
        _fencePanel.IsVisible = fence is not null;
        _groupPanel.IsVisible = !pick.IsEmpty && b is null && label is null && road is null && fence is null && item is null;
        int many = pick.Buildings.Count;
        _makeOne.IsEnabled = !_making && many > 0;
        _gMake.IsEnabled = !_making && many > 0;
        _gMake.IsVisible = many > 0;
        _gMake.Content = many == 1 ? "Put the picked building in the design" : $"Put the {many} picked buildings in the design";
        if (_threeD && !_view.Shown.Picked.SetEquals(pick.Buildings.Concat(pick.Items))) Show3D();
        _loadingBuilding = true;
        if (label is not null)
        {
            _lText.Text = label.Text;
            _lSize.Value = (decimal)label.Size;
            _lTurn.Value = (decimal)Math.Round(label.TurnDeg);
        }
        if (item is not null)
        {
            double sq = _map.SquareMm;
            var (iw, id) = item.Size(sq);
            double ih = CityItems.Height(item, sq) / sq;
            _iName.Text = item.Name;
            _iTurn.Value = (decimal)Math.Round(item.TurnDeg) % 360;
            var from = PhotonCanvas.Addon.Furniture.FurnitureCatalog.Find(item.CatalogId);
            _iInfo.Text = $"{iw * 5:0.#} × {id * 5:0.#} feet and {ih * 5:0.#} feet tall ({iw:0.##} × {id:0.##} squares, {item.WidthMm:0} × {item.DepthMm:0} mm)."
                + (from is not null ? $" From the {from.Theme.ToLowerInvariant()} {(from.Room == PhotonCanvas.Addon.Furniture.FurnitureCatalog.Town ? "town" : from.Room.ToLowerInvariant())} pieces." : "");
        }
        if (road is not null)
        {
            _rName.Text = road.Name;
            _rSurface.SelectedIndex = Math.Max(0, Array.IndexOf(RoadGrounds, road.Surface));
            _rWidth.Value = (decimal)road.Width;
            int bends = Math.Max(0, road.Points.Count - 2);
            _rInfo.Text = $"{road.Length:0.#} squares long ({road.Length * 5:0} feet), {bends} bend{(bends == 1 ? "" : "s")}."
                + (TownPieces.Bridges(_map).Count(x => x.Cells.Overlaps(road.Cells(_map.Across, _map.Down))) is var n && n > 0 ? $" It crosses water on {n} bridge{(n == 1 ? "" : "s")}." : "");
        }
        if (fence is not null)
        {
            _wKind.SelectedIndex = (int)fence.Kind;
            _wRuined.IsChecked = fence.Ruined;
            var lay = WallLayout.Of(_map, f => f.Id == fence.Id);
            int gates = lay.Gates.Count, caps = lay.Ends.Count(e => e.Kind == WallEndKind.Cap), towers = lay.Ends.Count(e => e.Kind == WallEndKind.Tower);
            _wInfo.Text = $"{fence.Length:0.#} squares long" + (fence.Closed ? ", all the way round" : "")
                + (gates > 0 ? $", {gates} gate{(gates == 1 ? "" : "s")}" : "")
                + (caps > 0 ? $", {caps} end cap{(caps == 1 ? "" : "s")}" : "")
                + (towers > 0 ? $", meets {towers} guard tower end{(towers == 1 ? "" : "s")}" : "") + ".";
        }
        if (_groupPanel.IsVisible)
        {
            var bs = _map.Buildings.Where(x => pick.Buildings.Contains(x.Id)).ToList();
            var ps = _map.Props.Where(x => pick.Props.Contains(x.Id)).ToList();
            var parts = new List<string>();
            if (bs.Count > 0) parts.Add($"{bs.Count} building{(bs.Count == 1 ? "" : "s")}");
            if (ps.Count > 0) parts.Add($"{ps.Count} tree{(ps.Count == 1 ? "" : "s")} and thing{(ps.Count == 1 ? "" : "s")}");
            if (pick.Items.Count > 0) parts.Add($"{pick.Items.Count} piece{(pick.Items.Count == 1 ? "" : "s")} of furniture");
            if (pick.Labels.Count > 0) parts.Add($"{pick.Labels.Count} label{(pick.Labels.Count == 1 ? "" : "s")}");
            if (pick.Fences.Count > 0) parts.Add($"{pick.Fences.Count} wall{(pick.Fences.Count == 1 ? "" : "s")} and fence{(pick.Fences.Count == 1 ? "" : "s")}");
            if (pick.Roads.Count > 0) parts.Add($"{pick.Roads.Count} road{(pick.Roads.Count == 1 ? "" : "s")}");
            _gInfo.Text = "Picked: " + string.Join(", ", parts) + ".";
            _groupBuildings.IsVisible = bs.Count > 0;
            _gType.Text = bs.Select(x => x.Type).Distinct().Count() == 1 ? bs[0].Type : "";
            _gDistrict.Text = bs.Select(x => x.District).Distinct().Count() == 1 ? bs[0].District : "";
            _groupProps.IsVisible = ps.Count > 0 && bs.Count == 0;
            if (ps.Count > 0)
            {
                _gProp.SelectedIndex = ps.Select(x => x.Kind).Distinct().Count() == 1 ? (int)ps[0].Kind : -1;
                _gPropSize.Value = (decimal)Math.Round(ps[0].Size, 1);
            }
        }
        _loadingBuilding = false;
        if (b is null) return;
        _loadingBuilding = true;
        _bName.Text = b.Name;
        _bType.Text = b.Type;
        _bDistrict.Text = b.District;
        _bTags.Text = string.Join(", ", b.Tags);
        var (w, d) = CityFootprint.Size(b);
        int floors = b.Design.Levels.Count;
        _bInfo.Text = $"{w} × {d} squares, {floors} floor{(floors == 1 ? "" : "s")}, {b.Design.Levels.Sum(l => l.Walls.Count)} squares of wall"
            + (CityFootprint.OnGrid(b) ? "" : b.TiltDeg != 0 ? $", tilted {b.TiltDeg:0.#}°" : ", off the grid")
            + (b.PrefabId is not null ? ". From a saved building." : ".");
        _bBuilt.Text = BuiltText(b);
        _bTilt.Text = b.TiltDeg.ToString("0.#");
        _bSquare.IsEnabled = !CityFootprint.OnGrid(b);
        if (b.Design.Roofs.Count > 0)
        {
            _bRoof.SelectedIndex = (int)b.Design.Roofs[0].Kind;
            _bCovering.SelectedItem = b.Design.Roofs[0].Covering;
        }
        _bRoof.IsEnabled = _bCovering.IsEnabled = b.Design.Roofs.Count > 0;
        _bDownLevel.IsEnabled = floors > 1;
        _bUp.IsEnabled = floors < HouseKitMaker.MaxLevels;
        _loadingBuilding = false;
    }

    /// <summary>Every picked building changed (unless <paramref name="skip"/>).</summary>
    void ChangePicked(Func<CityBuilding, CityBuilding> change, bool skip, string label)
    {
        if (_loadingBuilding || skip) return;
        var ids = _plan.Pick.Buildings;
        var m = _map with { Buildings = [.. _map.Buildings.Select(b => ids.Contains(b.Id) ? change(b) : b)] };
        if (m.Buildings.Zip(_map.Buildings).All(p => p.First.ToJsonEqual(p.Second))) return;
        Commit(m, label);
    }

    void ChangeProps(Func<Prop, Prop> change, string label)
    {
        if (_loadingBuilding) return;
        var ids = _plan.Pick.Props;
        if (ids.Count == 0) return;
        var m = _map with { Props = [.. _map.Props.Select(p => ids.Contains(p.Id) ? change(p) : p)] };
        if (m.Props.SequenceEqual(_map.Props)) return;
        Commit(m, label);
    }

    void ChangeItem(Func<CityItem, CityItem> change, string label)
    {
        if (_loadingBuilding || _loading || _plan.Pick.Items.Count != 1) return;
        var id = _plan.Pick.Items.First();
        var m = _map with { Items = [.. _map.Items.Select(i => i.Id == id ? change(i) : i)] };
        if (m.Items.SequenceEqual(_map.Items)) return;
        Commit(m, label);
    }

    void ChangeLabel(Func<CityLabel, CityLabel> change, string label)
    {
        if (_loadingBuilding || _plan.Pick.Labels.Count != 1) return;
        var id = _plan.Pick.Labels.First();
        var m = _map with { Labels = [.. _map.Labels.Select(l => l.Id == id ? change(l) : l)] };
        if (m.Labels.SequenceEqual(_map.Labels)) return;
        Commit(m, label);
    }

    void ChangeRoad(Func<CityRoad, CityRoad> change, string label)
    {
        if (_loadingBuilding || _plan.Pick.Roads.Count != 1) return;
        var id = _plan.Pick.Roads.First();
        if (_map.Road(id) is not { } r) return;
        var changed = change(r);
        if (changed == r) return;
        var m = _map with { Roads = [.. _map.Roads.Select(x => x.Id == id ? changed : x)] };
        // Wider, a road mustn't run into a building.
        if (changed.Width > r.Width && CityEdit.Hits(m, changed, CityEdit.BuildingCells(m)))
        {
            _status.Text = "That wide, the road would run into a building.";
            ShowPicked();
            return;
        }
        Commit(m, label);
    }

    void ChangeFence(Func<Fence, Fence> change, string label)
    {
        if (_loadingBuilding || _plan.Pick.Fences.Count != 1) return;
        var id = _plan.Pick.Fences.First();
        if (_map.FenceById(id) is not { } f) return;
        var changed = change(f);
        if (changed.Kind == f.Kind && changed.Ruined == f.Ruined) return;
        Commit(_map with { Fences = [.. _map.Fences.Select(x => x.Id == id ? changed : x)] }, label);
    }

    // ---- the town's buildings ----

    sealed record TownRow(CityBuilding Building)
    {
        public override string ToString()
        {
            var (w, d) = CityFootprint.Size(Building);
            var what = string.Join(", ", new[] { Building.Type, Building.District }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return $"{Building.Name}  ({(what.Length > 0 ? what + ", " : "")}{w} × {d})";
        }
    }

    void RefreshTown()
    {
        static List<string> Choices(string all, IEnumerable<string> values) =>
            [all, .. values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)];
        void Set(ComboBox c, List<string> items)
        {
            var was = c.SelectedItem as string;
            if (c.ItemsSource is List<string> old && old.SequenceEqual(items)) return;
            c.ItemsSource = items;
            c.SelectedItem = was is not null && items.Contains(was) ? was : items[0];
        }
        Set(_findType, Choices("Every type", _map.Buildings.Select(b => b.Type)));
        Set(_findDistrict, Choices("Every district", _map.Buildings.Select(b => b.District)));
        FillTownList();
        var byType = _map.Buildings.GroupBy(b => string.IsNullOrWhiteSpace(b.Type) ? "Other" : b.Type, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => $"{g.Count()} {Plural(g.Key.ToLowerInvariant(), g.Count())}");
        _townCounts.Text = _map.Buildings.Count == 0 ? "No buildings yet." : $"{_map.Buildings.Count} building{(_map.Buildings.Count == 1 ? "" : "s")}: {string.Join(", ", byType)}.";
        _legend.Children.Clear();
        foreach (var g in _map.Buildings.GroupBy(b => b.District.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key.Length == 0).ThenBy(g => g.Key))
        {
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new(3), Background = new SolidColorBrush(CityPlan.DistrictColour(g.Key)), Margin = new(0, 0, 8, 0) };
            _legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, new TextBlock { Text = $"{(g.Key.Length == 0 ? "No district" : g.Key)} ({g.Count()})" } } });
        }
    }

    /// <summary>A kind of building, more than one of them: houses, smithies, inns.</summary>
    static string Plural(string word, int n)
    {
        if (n == 1 || word.Length == 0) return word;
        if (word.EndsWith('y') && word.Length > 1 && !"aeiou".Contains(word[^2])) return word[..^1] + "ies";
        if (word.EndsWith('s') || word.EndsWith('x') || word.EndsWith("sh") || word.EndsWith("ch")) return word + "es";
        return word + "s";
    }

    void FillTownList()
    {
        string find = (_find.Text ?? "").Trim();
        string? type = _findType.SelectedIndex > 0 ? _findType.SelectedItem as string : null;
        string? district = _findDistrict.SelectedIndex > 0 ? _findDistrict.SelectedItem as string : null;
        bool Has(string? s) => s?.Contains(find, StringComparison.CurrentCultureIgnoreCase) == true;
        _townList.ItemsSource = _map.Buildings
            .Where(b => type is null || string.Equals(b.Type, type, StringComparison.OrdinalIgnoreCase))
            .Where(b => district is null || string.Equals(b.District, district, StringComparison.OrdinalIgnoreCase))
            .Where(b => find.Length == 0 || Has(b.Name) || Has(b.Type) || Has(b.District) || b.Tags.Any(Has))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => new TownRow(b)).ToList();
    }

    void ChangeBuilding(Func<CityBuilding, CityBuilding> change, string label)
    {
        if (_loadingBuilding || Picked() is not { } b) return;
        var nb = change(b);
        if (nb.ToJsonEqual(b)) return;
        Commit(_map.WithBuilding(nb), label);
    }

    /// <summary>A floor more on top: the top floor's walls, doors and windows again, the roofs moved up onto it.</summary>
    public static HouseDesign AddFloor(HouseDesign d)
    {
        if (d.Levels.Count == 0 || d.Levels.Count >= HouseKitMaker.MaxLevels) return d;
        var top = d.Levels[^1];
        var style = d.Style;
        var up = new Level
        {
            Walls = [.. top.Walls],
            // A door upstairs would open onto nothing: it's a window there.
            Openings = [.. top.Openings.Select(o => o.Kind == OpeningKind.Window ? o : Opening.NewWindow(style, o.X, o.Y))],
            WallLooks = [.. top.WallLooks],
        };
        int was = d.Levels.Count - 1;
        return d with { Levels = [.. d.Levels, up], Roofs = [.. d.Roofs.Select(r => r.Level == was ? r with { Level = was + 1 } : r)] };
    }

    /// <summary>The top floor taken off, its roofs moved down onto the floor under it.</summary>
    public static HouseDesign TakeFloor(HouseDesign d)
    {
        if (d.Levels.Count <= 1) return d;
        int top = d.Levels.Count - 1;
        return d with { Levels = [.. d.Levels.Take(top)], Roofs = [.. d.Roofs.Select(r => r.Level >= top ? r with { Level = top - 1 } : r)] };
    }

    /// <summary>New roofs over the top floor's rooms, a rectangle at a time, keeping the covering and shape of the first.</summary>
    public static HouseDesign RoofAgain(HouseDesign d)
    {
        if (d.Levels.Count == 0) return d;
        int top = d.Levels.Count - 1;
        var plan = d.Levels[top].Shape();
        var (w, dp) = CityFootprint.Extent(d);
        var cells = new HashSet<(int X, int Y)>();
        for (int y = 0; y < dp; y++)
            for (int x = 0; x < w; x++)
                if (plan.RoomAt(x + 0.5, y + 0.5) >= 0) cells.Add((x, y));
        var first = d.Roofs.FirstOrDefault();
        var roofs = BuildingGuess.Rectangles(cells).Select(r =>
        {
            var a = RoofArea.Over(top, new GridPt(r.X0, r.Y0), new GridPt(r.X1 + 1, r.Y1 + 1), d.Style);
            return first is null ? a : a with { Kind = first.Kind, Covering = first.Covering, PitchDeg = first.PitchDeg, EaveMm = first.EaveMm };
        }).ToList();
        return d with { Roofs = roofs };
    }

    void CopyBuilding()
    {
        if (Picked() is not { } b) return;
        var (w, d) = CityFootprint.Size(b);
        // Beside it, where there's room; otherwise below.
        var copy = b with { Id = Guid.NewGuid(), Name = b.Name + " (copy)", X = b.X + w + 1 + w <= _map.Across ? b.X + w + 1 : b.X, Y = b.X + w + 1 + w <= _map.Across ? b.Y : Math.Min(_map.Down - d, b.Y + d + 1) };
        Commit(_map.WithBuilding(copy), "Copy a building");
        _plan.Select(copy.Id);
    }

    string? HouseEditorData => PrefabShelf.FolderNextTo(_dataFolder) is { } f ? Path.GetDirectoryName(f) : null;

    async Task EditBuilding()
    {
        if (Picked() is not { } b) return;
        var editor = new HouseEditorWindow(_doc, _machine, _library, HouseEditorData, (Guid.NewGuid(), b.Design));
        await editor.ShowDialog(this);
        var changed = CityFootprint.Normalised(editor.Design);
        if (changed.ToJson() == b.Design.ToJson() || _map.Building(b.Id) is not { } now) return;
        Commit(_map.WithBuilding(now with { Design = changed, PrefabId = null }), "Change a building in the building editor");
    }

    async Task SaveAsPrefab()
    {
        if (Picked() is not { } b || PrefabShelf.FolderNextTo(_dataFolder) is not { } folder) return;
        _bSavePrefab.IsEnabled = false;
        _status.Text = $"Saving \"{b.Name}\"…";
        try
        {
            var p = new PrefabInfo { Id = Guid.NewGuid(), Name = b.Name, Type = b.Type, District = b.District, Tags = b.Tags, Theme = b.Design.Style.Theme, Design = b.Design };
            double longest = Longest;
            await Task.Run(() =>
            {
                // Its picture for the saved buildings, as the building editor makes it.
                byte[]? png = null;
                try { png = PrefabPictures.Png(HouseKitMaker.Make(p.Design, longest)); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException or IndexOutOfRangeException) { AppLog.Error("citybuilder picture", e); }
                PrefabShelf.Save(folder, p, png: png);
            });
            if (_map.Building(b.Id) is { } now) Commit(_map.WithBuilding(now with { PrefabId = p.Id }), "Save a building to reuse");
            LoadPrefabs();
            _status.Text = $"Saved \"{b.Name}\" with the building editor's saved buildings: put it down again with the Place tool.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save it: {e.Message}"; }
        finally { _bSavePrefab.IsEnabled = Picked() is not null; }
    }

    // ---- saved buildings ----

    sealed record PrefabRow(PrefabInfo Prefab)
    {
        public override string ToString()
        {
            var (w, d) = CityFootprint.Extent(Prefab.Design);
            string type = string.IsNullOrWhiteSpace(Prefab.Type) ? "" : $"{Prefab.Type}, ";
            return $"{(string.IsNullOrWhiteSpace(Prefab.Name) ? "Building" : Prefab.Name)}  ({type}{w} × {d}, {Prefab.Design.Levels.Count} floor{(Prefab.Design.Levels.Count == 1 ? "" : "s")})";
        }
    }

    void LoadPrefabs()
    {
        _prefabs = PrefabShelf.Load(PrefabShelf.FolderNextTo(_dataFolder));
        var types = _prefabs.Select(p => p.Type).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
        var was = _typeFilter.SelectedItem as string;
        _typeFilter.ItemsSource = new[] { "Every type" }.Concat(types).ToList();
        _typeFilter.SelectedItem = was is not null && types.Contains(was) ? was : "Every type";
        FillPrefabList();
        _prefabNote.Text = _prefabs.Count == 0
            ? "None saved yet: pick a building on the map and press \"Save as a building to reuse\" (Building tab), or save one in the building editor."
            : $"{_prefabs.Count} saved building{(_prefabs.Count == 1 ? "" : "s")}.";
    }

    void FillPrefabList()
    {
        string? type = _typeFilter.SelectedIndex > 0 ? _typeFilter.SelectedItem as string : null;
        _prefabList.ItemsSource = _prefabs.Where(p => type is null || string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase)).Select(p => new PrefabRow(p)).ToList();
    }

    // ---- making a town ----

    async Task MakeTown()
    {
        _tMake.IsEnabled = false;
        _status.Text = "Making a town…";
        try
        {
            var o = new TownOptions
            {
                Name = "New town",
                Across = (int)(_tAcross.Value ?? 48),
                Down = (int)(_tDown.Value ?? 36),
                Theme = _map.Theme,
                SquareMm = _map.SquareMm,
                Layout = (TownLayout)Math.Max(0, _tLayout.SelectedIndex),
                Density = _tDensity.Value,
                Walls = _tWalls.IsChecked == true,
                River = _tRiver.IsChecked == true,
                Square = _tSquare.IsChecked == true,
                Seed = _seed++,
                Prefabs = _tSaved.IsChecked == true ? _prefabs : [],
            };
            var (map, notes) = await Task.Run(() => TownMaker.Make(o));
            _file = null;
            _picture = null;
            _plan.SetPicture(null, 0, 0);
            Commit(map, "Make a town", refit: true);
            _tNotes.Text = string.Join("\n", notes.Select(n => "• " + n));
            _status.Text = "Made a town. Press Make a town again for another, or change anything you like.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't make a town: {e.Message}";
        }
        finally { _tMake.IsEnabled = true; }
    }

    async Task FillWithBuildings()
    {
        var district = _fDistrict.SelectedItem as string ?? "Residential";
        var box = _plan.LastBox;
        var (before, density, seed, prefabs) = (_map, _tDensity.Value, _seed++, _tSaved.IsChecked == true ? _prefabs : []);
        _fill.IsEnabled = false;
        try
        {
            var (m, added) = await Task.Run(() => TownMaker.Fill(before, district, density, seed, prefabs, box));
            if (added == 0) { _status.Text = "No room for buildings along the roads " + (box is null ? "on the map." : "in the box."); return; }
            if (!ReferenceEquals(before, _map)) { _status.Text = "The map changed while filling it: press Fill with buildings again."; return; }
            Commit(m, "Fill with buildings");
            _status.Text = $"Put up {added} building{(added == 1 ? "" : "s")} along the roads{(box is null ? "" : " in the box")} ({district.ToLowerInvariant()}).";
        }
        finally { _fill.IsEnabled = true; }
    }

    // ---- saving a picture ----

    (int X0, int Y0, int X1, int Y1)? BoxIf(CheckBox c) => c.IsChecked == true ? _plan.LastBox : null;

    void RefreshPictureInfo()
    {
        var (x0, y0, x1, y1) = BoxIf(_pBox) ?? (0, 0, _map.Across, _map.Down);
        int want = (int)(_pPx.Value ?? 100), px = TownPicture.FitPx(x1 - x0, y1 - y0, want);
        _pInfo.Text = $"{x1 - x0} × {y1 - y0} squares: {(x1 - x0) * px} × {(y1 - y0) * px} pixels"
            + (px < want ? $" (at most {px} pixels to a square for a map this big)." : ".")
            + (_pBox.IsChecked == true && _plan.LastBox is null ? " No box is dragged: the whole map." : "");
    }

    async Task SavePicture()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the map as a picture",
            SuggestedStartLocation = await Start(),
            SuggestedFileName = string.Concat(_map.Name.Split(Path.GetInvalidFileNameChars())) + ".png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG picture") { Patterns = ["*.png"] }, new FilePickerFileType("JPEG picture") { Patterns = ["*.jpg", "*.jpeg"] }, new FilePickerFileType("WebP picture") { Patterns = ["*.webp"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var map = _map;
        var o = new TownPictureOptions { PxPerSquare = (int)(_pPx.Value ?? 100), Grid = _pGrid.IsChecked == true, Labels = _pLabels.IsChecked == true, Names = _pNames.IsChecked == true, Box = BoxIf(_pBox) };
        _pSave.IsEnabled = false;
        _status.Text = "Drawing the picture…";
        try
        {
            var bytes = await Task.Run(() => TownPicture.Encode(map, o, path));
            await File.WriteAllBytesAsync(path, bytes);
            _status.Text = $"Saved {Path.GetFileName(path)} ({bytes.Length / 1048576.0:0.#} MB).";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't save the picture: {e.Message}";
        }
        finally { _pSave.IsEnabled = true; }
    }

    // ---- cutting the ground, bridges and scenery ----

    /// <summary>The most ground tiles made at once: a big town's ground is cut a part at a time.</summary>
    const int MaxGroundTiles = 400;
    /// <summary>The most trees cut at once (each is several pieces).</summary>
    const int MaxTrees = 150;

    void RefreshCut()
    {
        var box = BoxIf(_cBox);
        var (x0, y0, x1, y1) = box ?? (0, 0, _map.Across, _map.Down);
        int t = (int)(_cTile.Value ?? 4), tiles = (int)Math.Ceiling((x1 - x0) / (double)t) * (int)Math.Ceiling((y1 - y0) / (double)t);
        _cGroundInfo.Text = $"{x1 - x0} × {y1 - y0} squares ({(x1 - x0) * _map.SquareMm / 1000:0.##} × {(y1 - y0) * _map.SquareMm / 1000:0.##} m) in about {tiles} tiles."
            + (_cBox.IsChecked == true && _plan.LastBox is null ? " No box is dragged: the whole map." : "")
            + (tiles > MaxGroundTiles ? $" That's too many at once (at most {MaxGroundTiles}): drag a box round a part with Select and tick the box below." : "");
        _cGround.IsEnabled = tiles <= MaxGroundTiles;
        var sites = TownPieces.Bridges(_map, box);
        _cBridgesInfo.Text = sites.Count == 0 ? "No road crosses water" + (box is null ? "." : " in the box.") : $"{sites.Count} {Plural("place", sites.Count)} where a road crosses water.";
        _cBridges.IsEnabled = sites.Count > 0;
        var set = TownPieces.Scenery(_map, _map.Theme, box);
        var town = TownPieces.TownPropSet(_map, box);
        var what = (set?.Items.Select(i => $"{i.Count} × {PhotonCanvas.Addon.Scatter.Defaults.Info(i.Kind).Name.ToLowerInvariant()}" + (i.Kind == PhotonCanvas.Addon.Scatter.ItemKind.Fence ? $" ({i.WidthMm / _map.SquareMm:0.#} squares)" : "")) ?? [])
            .Concat(town?.Items.Select(i => $"{i.Count} × {(i.Name ?? "").ToLowerInvariant()}") ?? []).ToList();
        _cSceneryInfo.Text = what.Count == 0 ? "No wells, carts, crates, boulders, fences, troughs, stages, stalls, bars, notice boards, fountains, gallows, lamps or statues" + (box is null ? "." : " in the box.") : string.Join(", ", what) + ".";
        _cScenery.IsEnabled = what.Count > 0;
        var walls = TownPieces.Walls(_map, box);
        _cWallsInfo.Text = walls.Count == 0 ? "No town wall or guard towers" + (box is null ? "." : " in the box.")
            : string.Join(", ", new[] { (walls.Two, "section", " two squares long"), (walls.One, "section", " one square long"), (walls.CornersOut + walls.CornersIn, "corner", ""), (walls.Gates, "gatehouse", ""), (walls.Ends, "end", " with a turret"), (walls.Towers, "guard tower", "") }
                .Where(x => x.Item1 > 0).Select(x => $"{x.Item1} {Plural(x.Item2, x.Item1)}{x.Item3}")) + ".";
        _cWalls.IsEnabled = walls.Count > 0;
        var trees = TownPieces.TreeSet(_map, box);
        int treeCount = trees?.Items.Sum(i => i.Count) ?? 0;
        _cTreesInfo.Text = trees is null ? "No trees" + (box is null ? "." : " in the box.")
            : $"{treeCount} {Plural("tree", treeCount)}: " + string.Join(", ", trees.Items.Select(i => $"{i.Count} × {(i.Name ?? "").ToLowerInvariant()}")) + "."
              + (treeCount > MaxTrees ? $" That's a lot to cut at once (at most {MaxTrees}): drag a box round a part with Select and tick the box above." : "");
        _cTrees.IsEnabled = treeCount is > 0 and <= MaxTrees;
        var furniture = CityItems.Set(_map, box);
        int pieces = furniture?.Items.Sum(i => i.Count) ?? 0;
        _cFurnitureInfo.Text = furniture is null ? "No furniture" + (box is null ? "." : " in the box.")
            : $"{pieces} {Plural("piece", pieces)} of furniture: " + string.Join(", ", furniture.Items.Select(i => $"{i.Count} × {(i.Name ?? "").ToLowerInvariant()}")) + ".";
        _cFurniture.IsEnabled = pieces > 0;
    }

    // ---- the Furniture tool's pieces ----

    /// <summary>The catalog's pieces for the room and look picked, as picture buttons; the one the plan puts down lit.</summary>
    void FillFurniture()
    {
        var room = _fuRoom.SelectedItem as string ?? PhotonCanvas.Addon.Furniture.FurnitureCatalog.Town;
        var look = _fuLook.SelectedItem as string ?? _map.Theme;
        var entries = CityItems.Catalog(look, room);
        _fuPieces.Children.Clear();
        _fuButtons.Clear();
        var jobs = new List<(Avalonia.Controls.Image Image, PhotonCanvas.Addon.Furniture.CatalogEntry Entry)>();
        foreach (var e in entries)
        {
            var img = new Avalonia.Controls.Image { Stretch = Stretch.Uniform };
            var face = new Border { Width = 52, Height = 52, CornerRadius = new(5), ClipToBounds = true, Background = new SolidColorBrush(CityScene.GroundColour(Ground.Paving)), Child = img, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new ToggleButton
            {
                Classes = { "tool" }, Width = 72, Padding = new(3, 4), Margin = new(0, 0, 2, 2),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Top,
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children = { face, new TextBlock { Text = e.Name, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } },
                },
            };
            ToolTip.SetTip(b, e.About.Length > 0 ? $"{e.Name}: {e.About}" : e.Name);
            var entry = e;
            b.Click += (_, _) => { _plan.ToFurnish = entry; PickTool(CityTool.Furnish); SyncPalettes(); };
            _fuButtons.Add((e, b));
            jobs.Add((img, e));
            _fuPieces.Children.Add(b);
        }
        if (_plan.ToFurnish is null && entries.Count > 0) _plan.ToFurnish = entries[0];
        SyncPalettes();
        int version = ++_fuVersion;
        _fuPictures = DrawFurniture(jobs, version);
    }

    async Task DrawFurniture(List<(Avalonia.Controls.Image Image, PhotonCanvas.Addon.Furniture.CatalogEntry Entry)> jobs, int version)
    {
        foreach (var (img, e) in jobs)
        {
            byte[]? png;
            try { png = await Task.Run(() => PalettePictures.Furniture(e)); }
            catch (Exception x) when (x is ArgumentException or InvalidOperationException or IndexOutOfRangeException or ArithmeticException)
            {
                AppLog.Error("citybuilder palette", x);
                continue;
            }
            if (version != _fuVersion) return;
            using var stream = new MemoryStream(png);
            img.Source = new Avalonia.Media.Imaging.Bitmap(stream);
        }
    }

    void SyncFurniture()
    {
        foreach (var (e, b) in _fuButtons) b.IsChecked = _plan.ToFurnish?.Id == e.Id;
        _fuSyncing = true;
        _fuTurn.Value = (decimal)Math.Round(_plan.FurnishTurn) % 360;
        _fuSyncing = false;
    }

    bool _fuSyncing;

    async Task CutGround()
    {
        var before = _doc.State;
        var map = _map;
        var o = new GroundOptions
        {
            TileSquares = (int)(_cTile.Value ?? 4),
            ThicknessMm = (double)(_cThick.Value ?? 3),
            Join = (PhotonCanvas.Addon.FloorTiles.TileJoin)Math.Max(0, _cJoin.SelectedIndex),
            Grid = _cGrid.IsChecked == true,
            Outlines = _cOutlines.IsChecked == true,
            Box = BoxIf(_cBox),
        };
        _cGround.IsEnabled = false;
        _status.Text = "Making the ground's tiles…";
        try
        {
            var (state, kit, id, why) = await Task.Run(() =>
            {
                var (spec, kit) = GroundBoards.Make(map, o);
                if (!kit.Ok) return (before, kit, Guid.Empty, kit.Findings.First(f => f.Level == PhotonCanvas.Addon.Walls.Severity.Bad).Text);
                var big = PhotonCanvas.Addon.FloorTiles.TilePlacement.TooBig(kit, _machine).FirstOrDefault();
                if (big is not null) return (before, kit, Guid.Empty, big.Text);
                var (s, _, id) = PhotonCanvas.Addon.FloorTiles.TilePlacement.Place(before, kit, spec, _machine, new PhotonCanvas.Addon.FloorTiles.TilePlaceOptions());
                return (s, kit, id, (string?)null);
            });
            if (why is not null) { _status.Text = $"Couldn't make the ground's tiles: {why}"; return; }
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press it again."; return; }
            _doc.Commit("City ground", state, PhotonCanvas.Addon.FloorTiles.TilePlacement.ShapesOf(state, id));
            _status.Text = $"Put the ground in the design: {kit.Tiles.Count()} {Plural("tile", kit.Tiles.Count())} ({kit.PieceCount} pieces), nested onto the bed.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or ArithmeticException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't make the ground's tiles: {e.Message}";
        }
        finally { RefreshCut(); }
    }

    async Task CutBridges()
    {
        var map = _map;
        var sites = TownPieces.Bridges(map, BoxIf(_cBox));
        if (sites.Count == 0) return;
        _cBridges.IsEnabled = false;
        int done = 0;
        var problems = new List<string>();
        try
        {
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                _status.Text = $"Making bridge {i + 1} of {sites.Count}…";
                var before = _doc.State;
                var made = await Task.Run(() =>
                {
                    if (TownPieces.Bridge(site, map.Theme, map.SquareMm) is not { } b) return ((DocState, Guid)?)null;
                    var (s, _, id) = PhotonCanvas.Addon.Bridges.Placement.Place(before, b.Kit, b.Spec, _machine, new PhotonCanvas.Addon.Bridges.PlaceOptions());
                    return (s, id);
                });
                var (mx, my) = site.Middle;
                if (made is not { } r) { problems.Add($"the one at {mx:0}, {my:0} (no bridge suits it)"); continue; }
                if (!ReferenceEquals(_doc.State, before)) { problems.Add($"the one at {mx:0}, {my:0} (the design changed meanwhile)"); continue; }
                _doc.Commit("City bridge", r.Item1, PhotonCanvas.Addon.Bridges.Placement.ShapesOf(r.Item1, r.Item2));
                done++;
            }
            _status.Text = $"Put {done} {Plural("bridge", done)} in the design, each on pages of its own." + (problems.Count > 0 ? " Not made: " + string.Join("; ", problems) + "." : "");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or ArithmeticException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't make the bridges: {e.Message}";
        }
        finally { RefreshCut(); }
    }

    async Task CutScenery()
    {
        var box = BoxIf(_cBox);
        var set = TownPieces.Scenery(_map, _map.Theme, box);
        var town = TownPieces.TownPropSet(_map, box);
        if (set is null && town is null) return;
        var before = _doc.State;
        _cScenery.IsEnabled = false;
        _status.Text = "Making the scenery…";
        try
        {
            var (state, shapes, pieces, why) = await Task.Run(() =>
            {
                var state = before;
                var shapes = new HashSet<Guid>();
                int pieces = 0;
                // The scenery maker's wells, carts, barrels, boulders and fences.
                if (set is not null)
                {
                    var kit = PhotonCanvas.Addon.Scatter.ScatterKit.Make(set);
                    if (!kit.Ok) return (before, shapes, 0, kit.Items.SelectMany(i => i.Findings).First(f => f.Level == PhotonCanvas.Addon.Scatter.Severity.Bad).Text);
                    var (s, _, id) = PhotonCanvas.Addon.Scatter.Placement.Place(state, kit, set, _machine, new PhotonCanvas.Addon.Scatter.PlaceOptions());
                    state = s;
                    shapes.UnionWith(PhotonCanvas.Addon.Scatter.Placement.ShapesOf(s, id));
                    pieces += kit.PieceCount;
                }
                // The furniture maker's troughs, stages, stalls, bars and notice boards.
                if (town is not null)
                {
                    var kit = PhotonCanvas.Addon.Furniture.FurnitureKit.Make(town);
                    if (!kit.Ok) return (before, shapes, 0, kit.Items.SelectMany(i => i.Findings).First(f => f.Level == PhotonCanvas.Addon.Furniture.Severity.Bad).Text);
                    var (s, _, id) = PhotonCanvas.Addon.Furniture.Placement.Place(state, kit, town, _machine, new PhotonCanvas.Addon.Furniture.PlaceOptions());
                    state = s;
                    shapes.UnionWith(PhotonCanvas.Addon.Furniture.Placement.ShapesOf(s, id));
                    pieces += kit.PieceCount;
                }
                return (state, shapes, pieces, (string?)null);
            });
            if (why is not null) { _status.Text = $"Couldn't make the scenery: {why}"; return; }
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press it again."; return; }
            _doc.Commit("City scenery", state, shapes);
            _status.Text = $"Put the scenery in the design: {pieces} pieces.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or ArithmeticException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't make the scenery: {e.Message}";
        }
        finally { RefreshCut(); }
    }

    /// <summary>Puts a set of the furniture maker's pieces for the map (inside the box, if it's ticked) in the design.</summary>
    async Task CutFurniture(Button button, Func<CityMap, (int X0, int Y0, int X1, int Y1)?, PhotonCanvas.Addon.Furniture.FurnitureSet?> make, string label, string what)
    {
        if (make(_map, BoxIf(_cBox)) is not { } set) return;
        var before = _doc.State;
        button.IsEnabled = false;
        _status.Text = $"Making {what}…";
        try
        {
            var (state, kit, id, why) = await Task.Run(() =>
            {
                var kit = PhotonCanvas.Addon.Furniture.FurnitureKit.Make(set);
                if (!kit.Ok) return (before, kit, Guid.Empty, kit.Items.SelectMany(i => i.Findings).First(f => f.Level == PhotonCanvas.Addon.Furniture.Severity.Bad).Text);
                var (s, _, id) = PhotonCanvas.Addon.Furniture.Placement.Place(before, kit, set, _machine, new PhotonCanvas.Addon.Furniture.PlaceOptions());
                return (s, kit, id, (string?)null);
            });
            if (why is not null) { _status.Text = $"Couldn't make {what}: {why}"; return; }
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press it again."; return; }
            _doc.Commit(label, state, PhotonCanvas.Addon.Furniture.Placement.ShapesOf(state, id));
            _status.Text = $"Put {what} in the design: {kit.PieceCount} pieces, nested onto the bed.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or ArithmeticException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't make {what}: {e.Message}";
        }
        finally { RefreshCut(); }
    }

    // ---- the battle map ----

    async Task OpenPicture()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Battle map picture",
            FileTypeFilter =
            [
                new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _status.Text = $"Opening {Path.GetFileName(path)}…";
        try
        {
            var img = await Task.Run(() => RasterImage.Decode(File.ReadAllBytes(path)));
            await UsePicture(img, path);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            _status.Text = $"Couldn't open it: {e.Message}";
        }
    }

    void UseSelectedPicture()
    {
        var sel = _doc.Selection;
        if (_doc.State.Shapes.OfType<ImageShape>().FirstOrDefault(s => sel.Contains(s.Id)) is not { } pic) { _status.Text = "Select a picture in the design first."; return; }
        _ = UsePicture(pic.Source.Pixels, null);
    }

    /// <summary>Shows a battle map under the plan and finds its grid (also how the tests and the help pictures load one).</summary>
    public async Task UsePicture(RasterImage img, string? path)
    {
        _picture = img;
        _plan.SetPicture(PictureView.ToBitmap(img, 2400), img.Width, img.Height);
        await FindGrid(img);
        _map = _map with { Picture = (_map.Picture ?? new CityPicture()) with { Path = path } };
        _status.Text = "Now press Read the map (Battle map tab).";
    }

    async Task FindGrid(RasterImage img)
    {
        _status.Text = "Finding the grid…";
        var grid = await Task.Run(() => CityReader.FindGrid(img));
        _picNote.Text = grid is null
            ? "No grid lines found: type how many squares across the picture is."
            : $"Found a grid of {grid.SquarePx:0.#} pixel squares: {img.Width / grid.SquarePx:0.#} × {img.Height / grid.SquarePx:0.#} squares.";
        _loading = true;
        double px = grid?.SquarePx ?? Math.Round(img.Width / 30.0, 2);
        _px.Value = (decimal)px;
        _offX.Value = (decimal)(grid?.OffsetX ?? 0);
        _offY.Value = (decimal)(grid?.OffsetY ?? 0);
        _picAcross.Value = Math.Clamp((int)Math.Round(img.Width / px), 1, CityMap.MaxSide);
        _loading = false;
        _status.Text = "";
    }

    CityPicture PictureGrid() => new() { Path = _map.Picture?.Path, SquarePx = (double)(_px.Value ?? 70), OffsetX = (double)(_offX.Value ?? 0), OffsetY = (double)(_offY.Value ?? 0) };

    /// <summary>Reads the town from the picture shown: a new map (undo puts the old one back).</summary>
    public async Task ReadPicture()
    {
        if (_picture is not { } img) { _status.Text = "Open a battle map picture first."; return; }
        _read.IsEnabled = false;
        _status.Text = "Reading the map…";
        try
        {
            var o = new CityReadOptions
            {
                Buildings = (BuildingsShow)Math.Max(0, _shows.SelectedIndex),
                FindBuildings = _findBuildings.IsChecked == true,
                FindTrees = _findTrees.IsChecked == true,
                Sensitivity = _sensitivity.Value,
                LargestBuilding = (int)(_largest.Value ?? 120),
                Theme = _map.Theme,
                SquareMm = _map.SquareMm,
                Prefabs = _usePrefabs.IsChecked == true ? _prefabs : [],
            };
            var grid = PictureGrid();
            var res = await Task.Run(() => CityReader.Read(img, grid, o));
            _readNotes.Text = string.Join("\n", res.Notes.Select(n => "• " + n));
            Commit(res.Map with { Name = _map.Name }, "Read the battle map", refit: true);
            _status.Text = "Read the map. Check it over on the plan and in 3D.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or OutOfMemoryException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't read it: {e.Message}";
        }
        finally { _read.IsEnabled = true; }
    }

    // ---- making the pieces ----

    double Longest => Math.Max(50, Math.Min(_machine.FieldW, _machine.FieldH) - 4);

    bool _making;

    async Task Make(IReadOnlyList<CityBuilding> buildings)
    {
        if (_making || buildings.Count == 0) return;
        _making = true;
        _makeOne.IsEnabled = _makeAll.IsEnabled = _gMake.IsEnabled = false;
        int done = 0, pieces = 0;
        var problems = new List<string>();
        try
        {
            foreach (var b in buildings)
            {
                _status.Text = $"Making {b.Name}'s pieces ({done + 1} of {buildings.Count})…";
                var design = b.Design;
                double longest = Longest;
                var before = _doc.State;
                try
                {
                    var (kit, s, id) = await Task.Run(() =>
                    {
                        var kit = HouseKitMaker.Make(design, longest);
                        if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                        var (s, _, id) = HousePlacement.Place(before, kit, design, _machine, new PlaceOptions());
                        return (kit, s, id);
                    });
                    if (!ReferenceEquals(_doc.State, before)) { problems.Add($"{b.Name}: the design changed meanwhile"); continue; }
                    _doc.Commit($"Building: {b.Name}", s, HousePlacement.ShapesOf(s, id));
                    done++;
                    pieces += kit.PieceCount;
                }
                catch (InvalidOperationException e) { problems.Add($"{b.Name}: {e.Message}"); }
            }
            _status.Text = $"Put {done} building{(done == 1 ? "" : "s")} in the design ({pieces} pieces, each building on pages of its own)."
                + (problems.Count > 0 ? " Not made: " + string.Join("; ", problems) : "");
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("citybuilder", e);
            _status.Text = $"Couldn't put the buildings in: {e.Message}";
        }
        finally
        {
            _making = false;
            _makeAll.IsEnabled = _map.Buildings.Count > 0;
            _makeOne.IsEnabled = _gMake.IsEnabled = _plan.Pick.Buildings.Count > 0;
        }
    }

    // ---- files ----

    string? MapsFolder => _dataFolder is null ? null : Path.Combine(_dataFolder, "maps");
    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    CityMap? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? CityMap.FromJson(File.ReadAllText(f)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            var json = _map.ToJson();
            File.WriteAllText(f, json);
            if (_file is not null) File.WriteAllText(_file, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    async Task<IStorageFolder?> Start() => MapsFolder is { } m && Directory.CreateDirectory(m).Exists ? await StorageProvider.TryGetFolderFromPathAsync(m) : null;

    async Task OpenMap()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a town map",
            SuggestedStartLocation = await Start(),
            FileTypeFilter = [new FilePickerFileType("Town maps") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            if (CityMap.FromJson(await File.ReadAllTextAsync(path)) is not { } m) { _status.Text = "That isn't a town map."; return; }
            _file = path;
            _picture = null;
            _plan.SetPicture(null, 0, 0);
            if (m.Picture?.Path is { } pic && File.Exists(pic))
            {
                var img = await Task.Run(() => RasterImage.Decode(File.ReadAllBytes(pic)));
                _picture = img;
                _plan.SetPicture(PictureView.ToBitmap(img, 2400), img.Width, img.Height);
            }
            Commit(m, "Open a map", refit: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { _status.Text = $"Couldn't open it: {e.Message}"; }
    }

    async Task SaveMapAs()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the town map",
            SuggestedStartLocation = await Start(),
            SuggestedFileName = string.Concat(_map.Name.Split(Path.GetInvalidFileNameChars())) + ".json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Town maps") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, _map.ToJson());
            _file = path;
            _status.Text = $"Saved as {Path.GetFileName(path)}; changes are saved to it as you go.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save it: {e.Message}"; }
    }

    // ---- a map to start from ----

    /// <summary>A small village to show what the city builder does: a crossroads, a pond, a few houses, a tavern and a tower, fences and trees.</summary>
    public static CityMap Sample()
    {
        var m = CityMap.Empty(36, 26, "Sample village");
        var g = m.GroundGrid();
        for (int x = 0; x < 36; x++) { g[x, 12] = Ground.Road; g[x, 13] = Ground.Road; }
        for (int y = 0; y < 26; y++) { g[17, y] = Ground.Road; g[18, y] = Ground.Road; }
        for (int y = 9; y <= 16; y++) for (int x = 14; x <= 21; x++) if (g[x, y] != Ground.Road) g[x, y] = Ground.Paving;
        for (int y = 19; y <= 23; y++) for (int x = 3; x <= 9; x++) if ((x - 6) * (x - 6) + (y - 21) * (y - 21) * 1.6 <= 9) g[x, y] = Ground.Water;
        for (int y = 2; y <= 8; y++) for (int x = 26; x <= 34; x++) g[x, y] = Ground.Field;
        m = m.WithGround(g);
        var list = new List<CityBuilding>();
        void Add(int x, int y, int w, int d, int storeys, string type, string name, string district, string? covering = null)
        {
            var cells = new HashSet<(int X, int Y)>();
            for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) cells.Add((i, j));
            var towards = new HashSet<(int X, int Y)>();
            foreach (var (cx, cy) in cells)
                foreach (var n in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                    if (!cells.Contains(n) && m.GroundAt(n.Item1, n.Item2) is Ground.Road or Ground.Paving) towards.Add(n);
            var f = new SeenBuilding { Cells = cells, Storeys = storeys, Type = type, Covering = covering, DoorTowards = towards };
            if (BuildingGuess.Make(f, m.Theme, m.SquareMm) is { } made)
                list.Add(new CityBuilding { Name = name, Type = type, District = district, X = made.X, Y = made.Y, Design = made.Design });
        }
        Add(10, 8, 5, 4, 2, "Tavern", "The Crossed Keys", "Market", "Clay tiles");
        Add(21, 8, 4, 3, 1, "House", "Miller's house", "Farm", "Thatch");
        Add(10, 15, 4, 3, 1, "House", "Weaver's house", "Residential", "Thatch");
        Add(21, 15, 3, 3, 3, "Tower", "Watchtower", "Military", "Slate");
        Add(26, 15, 4, 3, 1, "Smithy", "Smithy", "Market", "Wooden shingles");
        Add(2, 8, 4, 3, 1, "House", "Cottage", "Residential", "Thatch");
        var fence = new Fence { Kind = FenceKind.Fence, Edges = [.. Enumerable.Range(26, 9).Select(x => new FenceEdge(x, 9, true)), .. Enumerable.Range(2, 7).Select(y => new FenceEdge(26, y, false))] };
        var hedge = new Fence { Kind = FenceKind.Hedge, Edges = [.. Enumerable.Range(0, 10).Select(x => new FenceEdge(x, 17, true))] };
        var rnd = new Random(7);
        var props = new List<Prop>();
        for (int i = 0; i < 22; i++)
        {
            double x = rnd.Next(0, 9) + 0.5, y = rnd.Next(0, 7) + 0.5;
            props.Add(new Prop { Kind = i % 3 == 0 ? PropKind.Pine : PropKind.Tree, X = x, Y = y, Size = 1 + rnd.NextDouble() * 0.5, TurnDeg = rnd.Next(360) });
        }
        props.Add(new Prop { Kind = PropKind.Well, X = 16, Y = 10.5 });
        props.Add(new Prop { Kind = PropKind.Cart, X = 20, Y = 11, TurnDeg = 90 });
        props.Add(new Prop { Kind = PropKind.Crates, X = 15.5, Y = 15.5 });
        props.Add(new Prop { Kind = PropKind.Lamp, X = 16.2, Y = 14.6 });
        props.Add(new Prop { Kind = PropKind.Bush, X = 11, Y = 19 });
        props.Add(new Prop { Kind = PropKind.Boulder, X = 31, Y = 21 });
        var labels = new List<CityLabel>
        {
            new() { Text = "King's Road", X = 6, Y = 13 },
            new() { Text = "Mill Lane", X = 18, Y = 4 },
            new() { Text = "Duck pond", X = 6, Y = 24.5, Size = 0.7 },
        };
        return m with { Buildings = list, Fences = [fence, hedge], Props = props, Labels = labels };
    }
}

static class CityBuildingCompare
{
    public static bool ToJsonEqual(this CityBuilding a, CityBuilding b) =>
        a.Name == b.Name && a.Type == b.Type && a.District == b.District && a.Tags.SequenceEqual(b.Tags) && a.X == b.X && a.Y == b.Y && a.Turn == b.Turn
        && a.ShiftX == b.ShiftX && a.ShiftY == b.ShiftY && a.TiltDeg == b.TiltDeg && a.PrefabId == b.PrefabId && a.Design.ToJson() == b.Design.ToJson();
}
