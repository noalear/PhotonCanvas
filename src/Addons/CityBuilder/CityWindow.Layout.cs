using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.App;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// The city builder's layout, a map maker's: along the top the toolbar (the map's file, undo, snapping, the plan
/// or 3D, and the window's jobs: making a town, reading a battle map, saving a picture, cutting); down the left
/// the tools, and beside them what the tool picked puts down, in pictures; in the middle the plan or the 3D view;
/// on the right what's picked, the town's buildings and the map's settings (or the job opened on the toolbar);
/// along the bottom where the pointer is, what was last done and how big the map is.
/// </summary>
public sealed partial class CityWindow
{
    // Icons of the tools (24 × 24, filled) that the program has none of.
    const string HouseIcon = "M12 3 2 11.5h3V21h5.5v-6h3v6H19v-9.5h3z";
    const string StampIcon = "M9 2h6v5.5L13.5 11H19a2 2 0 0 1 2 2v3H3v-3a2 2 0 0 1 2-2h5.5L9 7.5zM3 18h18v3H3z";
    const string BrushIcon = "M21.4 2.6a1.4 1.4 0 0 0-2 0L9.6 12.4l2 2 9.8-9.8a1.4 1.4 0 0 0 0-2zM7.6 13.9C5.6 13.9 4 15.5 4 17.5c0 1.3-.8 2.3-2 2.8 1.2 1.1 2.7 1.7 4.4 1.7 2.8 0 5-2.2 5-5 0-.4 0-.7-.1-1z";
    const string BucketIcon = "M10.5 2.5 3 10a1.4 1.4 0 0 0 0 2l7 7a1.4 1.4 0 0 0 2 0l7.5-7.5zM10.5 5.3l5.9 5.9H4.6zM20.5 14.5s-2 2.4-2 3.9a2 2 0 0 0 4 0c0-1.5-2-3.9-2-3.9z";
    const string RoadIcon = "M8.5 2h2.2L8 22H3.5zM13.3 2h2.2l5 20H16zM11.2 3.5h1.6V7h-1.6zM11.1 10h1.8v4h-1.8zM11 17h2v5h-2z";
    const string WallIcon = "M2 4h4v3h3V4h6v3h3V4h4v17H2zM9.5 21v-4.5a2.5 2.5 0 0 1 5 0V21z";
    const string TreeIcon = "M12 2a6 6 0 0 0-5.8 7.6A5 5 0 0 0 8 19h3v3h2v-3h3a5 5 0 0 0 1.8-9.4A6 6 0 0 0 12 2z";
    const string EraserIcon = "M14.6 3.4a2 2 0 0 1 2.8 0l4.2 4.2a2 2 0 0 1 0 2.8L11.5 20.5H20V22H8.6l-5.2-5.2a2 2 0 0 1 0-2.8zM8.8 9.2l-4 4 4.4 4.4h2.2l3.8-3.8z";
    const string ChairIcon = "M6 2h2v9H6zM16 2h2v9h-2zM8 4h8v2H8zM4 11h16v3H4zM5 14h2v8H5zM17 14h2v8h-2z";
    const string CloseIcon = "M6.4 5 5 6.4 10.6 12 5 17.6 6.4 19 12 13.4 17.6 19 19 17.6 13.4 12 19 6.4 17.6 5 12 10.6z";

    /// <summary>The tools down the left, in order (a gap before each group), with the key that picks each (the numbers, then E for Erase).</summary>
    static readonly (CityTool Tool, string Name, string Icon, bool Gap, string Tip)[] RailTools =
    [
        (CityTool.Select, "Select", "IcCursor", false, "Click a building, road, wall, tree, piece of furniture or label to pick it (Ctrl and click to pick more), or drag a box round things. Drag to move what's picked (as far at a time as Snap says), or a road's or wall's handles to stretch and bend it; R turns it a quarter turn, Ctrl+R 15°, Delete takes it away. Double-click a building to open it in the building editor."),
        (CityTool.Building, "Building", HouseIcon, true, "Drag out a building's footprint: walls go round it, with a door towards the road, windows and a roof. The right button takes a building away."),
        (CityTool.Place, "Place", StampIcon, false, "Put down one of your saved buildings: pick it beside the tools, then click the map. R, the right button or Ctrl + the wheel turns it first."),
        (CityTool.Ground, "Paint", BrushIcon, true, "Paint the ground: pick what beside the tools, and the brush's size. The right button paints grass."),
        (CityTool.Fill, "Fill", BucketIcon, false, "Click to fill a patch of one ground with another."),
        (CityTool.Road, "Road", RoadIcon, true, "Drag from one point to another to lay a road (round one corner; hold Ctrl for a straight one). A street is two squares wide, a high road four. Roads stop at buildings and cross water on bridges. The right button takes a road away."),
        (CityTool.Fence, "Wall", WallIcon, false, "Drag a fence, hedge, wall or town wall, as a line or a rectangle; it bends in eighths of a turn unless you hold Ctrl. Its ends are capped and a road through it is a gate. The right button cuts a gap in one."),
        (CityTool.Prop, "Things", TreeIcon, true, "Click to put down a tree or a thing: rocks, wells, carts, lamps, market stalls, a stage, a fountain, a guard tower… The right button takes them away."),
        (CityTool.Furnish, "Furniture", ChairIcon, false, "Put down the furniture maker's pieces, true to size, on a fine grid (tenths of a square): tables and benches outside the inn, the stocks, chests, barrels, a weapon rack… Click one that's down to drag it; R turns the next one 45°. The right button takes one away."),
        (CityTool.Label, "Label", "IcText", false, "Click to write a label on the map: a district's name, a number for your key. R turns it. The right button takes one away."),
        (CityTool.Erase, "Erase", EraserIcon, true, "Drag over trees, roads, walls, labels and buildings to take them away."),
    ];

    readonly TabControl _palette = new() { Padding = new(0) };
    readonly Dictionary<CityTool, TabItem> _paletteItems = [];
    /// <summary>Each picture palette's buttons, set from what the plan puts down.</summary>
    readonly List<Action> _paletteSync = [];
    readonly List<(Image Image, Func<byte[]> Picture)> _pictureJobs = [];

    readonly TabControl _side = new() { Width = 348 };
    TabItem _pickedTab = null!, _townTab = null!, _mapTab = null!, _makePage = null!, _readPage = null!, _savePage = null!, _cutPage = null!, _lastTab = null!;
    readonly Dictionary<TabItem, ToggleButton> _taskButtons = [];

    readonly ToggleButton _planButton = Seg("Plan", "first"), _view3dButton = Seg("3D", "last");
    readonly Grid _planBox = new(), _box3d = new() { IsVisible = false };
    bool _threeD;
    readonly TextBlock _where = new() { Classes = { "muted" }, MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };

    Control BuildLayout()
    {
        var middle = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto") };
        middle.Children.Add(Rail());
        middle.Children.Add(PalettePanel().With(c => Grid.SetColumn(c, 1)));
        middle.Children.Add(Views().With(c => Grid.SetColumn(c, 2)));
        middle.Children.Add(SidePanel().With(c => Grid.SetColumn(c, 3)));
        var root = new Grid { RowDefinitions = new("Auto,*,Auto") };
        root.Children.Add(Toolbar());
        root.Children.Add(middle.With(c => Grid.SetRow(c, 1)));
        root.Children.Add(StatusBar().With(c => Grid.SetRow(c, 2)));
        return root;
    }

    // ---- the toolbar ----

    Control Toolbar()
    {
        static Control Sep() => new Border { Width = 1, Background = Brush("Line"), Margin = new(8, 3) };
        void Icon(Button b, string icon, string tip)
        {
            b.Content = new PathIcon { Data = IconOrPath(icon), Width = 16, Height = 16 };
            b.Classes.Add("icon");
            b.HorizontalAlignment = HorizontalAlignment.Left;
            ToolTip.SetTip(b, tip);
            Avalonia.Automation.AutomationProperties.SetName(b, tip);
        }
        Icon(_newMap, "IcNew", "New map: all grass, the size on the Map tab");
        Icon(_openMap, "IcOpen", "Open a map…");
        Icon(_saveMap, "IcSave", "Save the map as… (changes go on being saved to it)");
        Icon(_undoButton, "IcUndo", "Undo (Ctrl+Z)");
        Icon(_redoButton, "IcRedo", "Redo (Ctrl+Y)");

        var snap = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        snap.Children.Add(new TextBlock { Text = "Snap", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        var snaps = new (CitySnap Snap, string Name, string Tip)[]
        {
            (CitySnap.Squares, "Whole", "Snap to whole squares"),
            (CitySnap.HalfSquares, "Half", "Snap to half squares"),
            (CitySnap.Fine, "Fine", "Snap to tenths of a square (six inches), the furniture's grid"),
            (CitySnap.Free, "Free", "Put things anywhere"),
        };
        var snapButtons = new List<(CitySnap, ToggleButton)>();
        for (int i = 0; i < snaps.Length; i++)
        {
            var (s, name, tip) = snaps[i];
            var b = Seg(name, i == 0 ? "first" : i == snaps.Length - 1 ? "last" : null);
            b.MinWidth = 52;
            ToolTip.SetTip(b, tip + ": where what you put down and drag lands. Roads keep to their middles.");
            b.Click += (_, _) => { _plan.Snap = s; SyncPalettes(); };
            snapButtons.Add((s, b));
            snap.Children.Add(b);
        }
        _paletteSync.Add(() => { foreach (var (s, b) in snapButtons) b.IsChecked = _plan.Snap == s; });

        var view = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new[] { _planButton, _view3dButton }) { b.MinWidth = 54; view.Children.Add(b); }
        ToolTip.SetTip(_planButton, "The map from above, to draw on");
        ToolTip.SetTip(_view3dButton, "The town put together in 3D: drag to turn it round, scroll to zoom");
        _planButton.Click += (_, _) => ShowView(false);
        _view3dButton.Click += (_, _) => ShowView(true);

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new Control[] { _newMap, _openMap, _saveMap, Sep(), _undoButton, _redoButton, Sep(), snap, Sep(), view }) left.Children.Add(c);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(TaskButton(_makePage, "Town maker", "IcWand", "Make a whole town for you: roads, a market square, buildings of the right kinds, walls, a river, trees"));
        right.Children.Add(TaskButton(_readPage, "Battle map", "IcImage", "Read a town from a battle map picture"));
        right.Children.Add(TaskButton(_savePage, "Picture", "IcExport", "Save the map as a battle map picture, for a virtual table or to print"));
        right.Children.Add(TaskButton(_cutPage, "Cut", "IcCut", "Put the buildings, ground, bridges, walls, trees and scenery in the design to cut"));
        right.Children.Add(Sep());
        right.Children.Add(ContextHelp.HelpButton(this));

        var bar = new DockPanel { LastChildFill = true, Margin = new(10, 6) };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);
        return new Border { Background = Brush("Bg0"), BorderBrush = Brush("Line"), BorderThickness = new(0, 0, 0, 1), Child = bar };
    }

    static ToggleButton Seg(string text, string? end)
    {
        var b = new ToggleButton { Content = text, Classes = { "seg" } };
        if (end is not null) b.Classes.Add(end);
        return b;
    }

    ToggleButton TaskButton(TabItem page, string text, string icon, string tip)
    {
        var b = new ToggleButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new PathIcon { Data = IconOrPath(icon), Width = 15, Height = 15 }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => { if (_side.SelectedItem == page) ClosePage(); else _side.SelectedItem = page; };
        _taskButtons[page] = b;
        return b;
    }

    // ---- the tools ----

    Control Rail()
    {
        var rail = new StackPanel { Margin = new(6, 8, 6, 8) };
        for (int i = 0; i < RailTools.Length; i++)
        {
            var (t, name, icon, gap, tip) = RailTools[i];
            if (gap) rail.Children.Add(new Border { Height = 1, Background = Brush("Line"), Margin = new(6, 4, 6, 6) });
            var b = new ToggleButton
            {
                Classes = { "tool" }, Width = 62, Padding = new(2, 6), Margin = new(0, 0, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new PathIcon { Data = IconOrPath(icon), Width = 20, Height = 20 },
                        new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                },
            };
            string key = i < 10 ? $"{(i + 1) % 10}" : t == CityTool.Erase ? "E" : "";
            ToolTip.SetTip(b, key.Length > 0 ? $"{name} (key {key}): {tip}" : $"{name}: {tip}");
            var tool = t;
            b.Click += (_, _) => PickTool(tool);
            _tools[t] = b;
            rail.Children.Add(b);
        }
        return new Border
        {
            Background = Brush("Bg0"), BorderBrush = Brush("Line"), BorderThickness = new(0, 0, 1, 0),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = rail },
        };
    }

    /// <summary>The tool picked by its number key (1 for the first tool, 0 for the tenth).</summary>
    static CityTool? ToolForKey(Key k) => k switch
    {
        >= Key.D1 and <= Key.D9 when k - Key.D1 < RailTools.Length => RailTools[k - Key.D1].Tool,
        Key.D0 when RailTools.Length >= 10 => RailTools[9].Tool,
        >= Key.NumPad1 and <= Key.NumPad9 when k - Key.NumPad1 < RailTools.Length => RailTools[k - Key.NumPad1].Tool,
        Key.NumPad0 when RailTools.Length >= 10 => RailTools[9].Tool,
        Key.E => CityTool.Erase,
        _ => null,
    };

    // ---- what the tool puts down ----

    Control PalettePanel()
    {
        void Add(CityTool t, string title, string about, params Control[] body)
        {
            var stack = new StackPanel { Spacing = 8, Margin = new(12, 12, 12, 12) };
            stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
            if (about.Length > 0) stack.Children.Add(Hint(about));
            foreach (var c in body) stack.Children.Add(c);
            var item = new TabItem { Header = title, IsVisible = false, Content = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack } };
            _paletteItems[t] = item;
            _palette.Items.Add(item);
        }

        Add(CityTool.Select, "Select", "Pick things on the plan to move, turn, copy or change them (on the Picked tab, right).",
            Tips(
                "Click a thing to pick it; Ctrl and click picks more.",
                "Drag from an empty spot to pick all in a box.",
                "Drag what's picked to move it, as far at a time as Snap says (arrow keys too; Ctrl and an arrow a tenth of a square).",
                "R turns it a quarter turn; Ctrl+R turns each thing 15° on its own spot (a building tilts). Delete takes it away, Ctrl+D copies it beside itself.",
                "Pick a road or wall and drag its handles to stretch and bend it.",
                "Double-click a building to open it in the building editor."),
            Explained("Fill with buildings", "Buildings along the roads on the free grass, of the kinds the district has: in the last box you dragged, or all over the map.",
                Labelled("District (what kinds of building)", _fDistrict), _fill));

        Add(CityTool.Building, "Building", "Drag out a rectangle on the plan: a building stands on it, with walls round it, a door towards the road, windows and a roof, in the map's look.",
            Labelled("Floors", _storeysNew),
            Hint("The right button takes a building away. To put down one of your saved buildings, use Place."));

        _prefabList.SelectionMode = SelectionMode.Single;
        _prefabList.Height = double.NaN;
        _prefabList.MinHeight = 60;
        _prefabList.ItemTemplate = new FuncDataTemplate<PrefabRow>((r, _) => r is null ? new TextBlock() : PrefabTile(r), true);
        Add(CityTool.Place, "Place", "Your saved buildings (Save as a building to reuse, here or in the building editor). Pick one, then click the map to put it down; R turns it first.",
            Labelled("Type", _typeFilter), _prefabList, _prefabNote, _refreshPrefabs);

        Add(CityTool.Ground, "Paint", "Pick a ground and paint it on with the brush. The right button paints grass.",
            GroundSwatches(), Labelled("Brush (squares)", _brush));

        Add(CityTool.Fill, "Fill", "Pick a ground, then click a patch of one ground to fill it all with this. The right button fills it with grass.",
            GroundSwatches());

        var lane = Chip("Lane (2)", "A lane or street: two squares wide (ten feet)");
        var high = Chip("High road (4)", "A high road: four squares wide (twenty feet)");
        lane.Click += (_, _) => { _roadWidth.Value = 2; SyncPalettes(); };
        high.Click += (_, _) => { _roadWidth.Value = 4; SyncPalettes(); };
        _paletteSync.Add(() => { lane.IsChecked = _plan.RoadWidth == 2; high.IsChecked = _plan.RoadWidth == 4; });
        Add(CityTool.Road, "Road", "Drag from one point to another: the road runs there round one corner (hold Ctrl for a straight one), with a name of its own along it. It stops at a building in its way and crosses water on a bridge.",
            Heading("Laid with"),
            Swatches(RoadGrounds, g => g switch { Ground.Road => "Cobbles", Ground.Dirt => "Dirt track", Ground.Paving => "Paving", _ => CityMap.Label(g) },
                g => () => PalettePictures.Road(g), CityScene.GroundColour, () => _plan.RoadGround, g => _plan.RoadGround = g),
            Heading("How wide"),
            new WrapPanel { Children = { lane, high } },
            Labelled("Width (squares)", _roadWidth));

        var line = Seg("A line", "first");
        var rect = Seg("A rectangle", "last");
        foreach (var b in new[] { line, rect }) b.MinWidth = 100;
        line.Click += (_, _) => { _plan.FenceRectangle = false; SyncPalettes(); };
        rect.Click += (_, _) => { _plan.FenceRectangle = true; SyncPalettes(); };
        _paletteSync.Add(() => { line.IsChecked = !_plan.FenceRectangle; rect.IsChecked = _plan.FenceRectangle; });
        Add(CityTool.Fence, "Wall", "Drag to put it up: from one point to the next, or a rectangle all the way round. Its ends get end caps (a ruined one's are left broken), and a road through it is a gate.",
            Swatches(Enum.GetValues<FenceKind>(), CityMap.Label, k => () => PalettePictures.Wall(k), _ => CityScene.GroundColour(Ground.Grass), () => _plan.FenceKind, k => _plan.FenceKind = k),
            Heading("Shape"),
            new StackPanel { Orientation = Orientation.Horizontal, Children = { line, rect } },
            _fenceRuined);

        Control Things(string heading, params PropKind[] kinds) => new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Heading(heading),
                Swatches(kinds, CityMap.Label, k => () => PalettePictures.Thing(k), k => CityScene.GroundColour(k is PropKind.Tree or PropKind.Pine or PropKind.Bush or PropKind.Boulder or PropKind.GuardTower ? Ground.Grass : Ground.Paving),
                    () => _plan.PropKind, k => _plan.PropKind = k),
            },
        };
        Add(CityTool.Prop, "Trees and things", "Pick one and click the map to put it down. Trees and small things go to quarter squares; a guard tower put on a town wall stands astride it.",
            Things("Trees and rocks", PropKind.Tree, PropKind.Pine, PropKind.Bush, PropKind.Boulder),
            Things("In the street", PropKind.Well, PropKind.Cart, PropKind.Crates, PropKind.Lamp, PropKind.Statue, PropKind.Trough, PropKind.NoticeBoard, PropKind.Fountain),
            Things("On the square", PropKind.Stall, PropKind.Bar, PropKind.Stage, PropKind.Gallows),
            Things("Defences", PropKind.GuardTower));

        _fuPieces.Margin = new(0, 2, 0, 0);
        Add(CityTool.Furnish, "Furniture", "The furniture maker's pieces, true to size for the map's squares. Pick one, then click the map to put it down on the fine grid (a tenth of a square, six inches) and drag it into place before you let go. Drag one that's down to move it; the right button takes one away.",
            Labelled("Where it goes", _fuRoom), Labelled("Look", _fuLook), _fuPieces,
            Labelled("Turn (degrees: R turns it 45°, Ctrl + the wheel 15°)", _fuTurn),
            Hint("Wells, market stalls, troughs, a stage and the town's other big pieces are under Things: set Snap to Fine to put those on the fine grid too. The furniture is cut on the Cut page."));
        _paletteSync.Add(SyncFurniture);

        Add(CityTool.Label, "Label", "Click to write it on the map: a street's name, a district's, a number for your key. Labels always read the right way up.",
            Labelled("Text", _labelText), Labelled("Size (squares)", _labelSize), Labelled("Turn (degrees: R turns it 15° more, Shift+R back)", _labelTurn));

        Add(CityTool.Erase, "Erase", "Drag over the plan to take away the trees, things, furniture, labels, roads, walls and buildings under the brush.");

        _palette.SelectionChanged += (_, e) =>
        {
            if (e.Source != _palette) return;
            if (_palette.SelectedItem is TabItem t && _paletteItems.FirstOrDefault(p => p.Value == t).Key is var tool && tool != _plan.Tool && _paletteItems.ContainsKey(tool)) PickTool(tool);
        };
        return new Border { Width = 252, Background = Brush("Bg1"), BorderBrush = Brush("Line"), BorderThickness = new(0, 0, 1, 0), Child = _palette };
    }

    static Control Tips(params string[] lines)
    {
        var s = new StackPanel { Spacing = 4 };
        foreach (var l in lines) s.Children.Add(new TextBlock { Text = "• " + l, TextWrapping = TextWrapping.Wrap, Classes = { "hint" } });
        return s;
    }

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new(0, 4, 0, 0) };

    static ToggleButton Chip(string text, string tip)
    {
        var b = new ToggleButton { Content = text, Classes = { "chip" }, Margin = new(0, 0, 4, 4) };
        ToolTip.SetTip(b, tip);
        return b;
    }

    WrapPanel GroundSwatches() => Swatches(Enum.GetValues<Ground>(), CityMap.Label, g => () => PalettePictures.Ground(g), CityScene.GroundColour, () => _plan.Paint, g => _plan.Paint = g);

    /// <summary>A picture button for each choice; the one the plan has is lit.</summary>
    WrapPanel Swatches<T>(IEnumerable<T> values, Func<T, string> name, Func<T, Func<byte[]>> picture, Func<T, Color> colour, Func<T> current, Action<T> pick) where T : struct
    {
        var panel = new WrapPanel();
        var buttons = new List<(T Value, ToggleButton Button)>();
        foreach (var v in values)
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            var face = new Border { Width = 52, Height = 52, CornerRadius = new(5), ClipToBounds = true, Background = new SolidColorBrush(colour(v)), Child = img, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new ToggleButton
            {
                Classes = { "tool" }, Width = 72, Padding = new(3, 4), Margin = new(0, 0, 2, 2),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Top,
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children = { face, new TextBlock { Text = name(v), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } },
                },
            };
            ToolTip.SetTip(b, name(v));
            var value = v;
            b.Click += (_, _) => { pick(value); SyncPalettes(); };
            buttons.Add((v, b));
            _pictureJobs.Add((img, picture(v)));
            panel.Children.Add(b);
        }
        _paletteSync.Add(() => { var now = current(); foreach (var (v, b) in buttons) b.IsChecked = EqualityComparer<T>.Default.Equals(v, now); });
        return panel;
    }

    /// <summary>Lights the palettes' buttons for what the plan puts down.</summary>
    void SyncPalettes()
    {
        foreach (var s in _paletteSync) s();
    }

    /// <summary>Draws the palettes' pictures (the first time, a moment after the window opens).</summary>
    async Task LoadPictures()
    {
        var jobs = _pictureJobs.ToList();
        List<byte[]?> made;
        try
        {
            made = await Task.Run(() => jobs.Select(j =>
            {
                try { return j.Picture(); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or ArithmeticException)
                {
                    AppLog.Error("citybuilder palette", e);
                    return null;
                }
            }).ToList());
        }
        catch (Exception e) when (e is AggregateException or OperationCanceledException) { return; }
        var bitmaps = new Dictionary<byte[], Bitmap>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < jobs.Count; i++)
        {
            if (made[i] is not { } png) continue;
            if (!bitmaps.TryGetValue(png, out var bmp))
            {
                using var s = new MemoryStream(png);
                bitmaps[png] = bmp = new Bitmap(s);
            }
            jobs[i].Image.Source = bmp;
        }
    }

    static readonly Dictionary<string, Bitmap?> Thumbnails = [];

    /// <summary>A saved building in the Place list: its picture, name and size.</summary>
    static Control PrefabTile(PrefabRow r)
    {
        var p = r.Prefab;
        Bitmap? pic = null;
        if (p.Thumbnail is { } path)
        {
            lock (Thumbnails)
                if (!Thumbnails.TryGetValue(path, out pic))
                {
                    try { pic = File.Exists(path) ? new Bitmap(path) : null; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { pic = null; }
                    Thumbnails[path] = pic;
                }
        }
        var (w, d) = CityFootprint.Extent(p.Design);
        var face = new Border
        {
            Width = 48, Height = 48, CornerRadius = new(5), ClipToBounds = true, Background = Brush("Bg3"), Margin = new(0, 0, 8, 0),
            Child = pic is null ? new PathIcon { Data = IconOrPath(HouseIcon), Width = 22, Height = 22, Opacity = 0.6 } : new Image { Source = pic, Stretch = Stretch.UniformToFill },
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = string.IsNullOrWhiteSpace(p.Name) ? "Building" : p.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{(string.IsNullOrWhiteSpace(p.Type) ? "" : p.Type + ", ")}{w} × {d}, {p.Design.Levels.Count} floor{(p.Design.Levels.Count == 1 ? "" : "s")}", Classes = { "muted" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        return new DockPanel { Children = { face.With(f => DockPanel.SetDock(f, Dock.Left)), text } };
    }

    // ---- the plan and the 3D view ----

    Control Views()
    {
        _planBox.Children.Add(_plan);
        _planBox.Children.Add(ZoomButtons());
        var shows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var c in new[] { _vBuilt, _vDoors, _vRoofs, _vProps, _vOpenings }) shows.Children.Add(c);
        shows.Children.Add(_vMaking);
        ToolTip.SetTip(_vBuilt, "Each building as it'll be once it's cut and put together, true to size: its corner columns, its roofs' overhang, its doors, shutters and lamps. Untick for plain blocks the size of its plan.");
        ToolTip.SetTip(_vDoors, "The doors and shutters swung open, as far as they go.");
        ToolTip.SetTip(_vOpenings, "Doors and windows cut in the plain blocks (buildings not shown as built).");
        var hud = new Border { Classes = { "hud" }, Padding = new(10, 2), Margin = new(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = shows };
        _box3d.Children.Add(_view);
        _box3d.Children.Add(hud);
        return new Border
        {
            Margin = new(8), CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1),
            Child = new Grid { Children = { _planBox, _box3d } },
        };
    }

    Control ZoomButtons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var (icon, tip, act) in new (string, string, Action)[]
        {
            ("IcFit", "See the whole map (F)", _plan.Fit),
            ("IcZoomIn", "Zoom in (or scroll)", () => _plan.ZoomBy(1.25)),
            ("IcZoomOut", "Zoom out", () => _plan.ZoomBy(0.8)),
        })
        {
            var b = new Button
            {
                Classes = { "icon" }, Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new(0), Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Content = new PathIcon { Data = IconOrPath(icon), Width = 15, Height = 15 },
                Cursor = new Cursor(StandardCursorType.Arrow),
            };
            ToolTip.SetTip(b, tip);
            Avalonia.Automation.AutomationProperties.SetName(b, tip);
            b.Click += (_, _) => act();
            row.Children.Add(b);
        }
        return new Border
        {
            Background = Brush("HudBg"), BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(7), Padding = new(2), Margin = new(0, 0, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = row,
        };
    }

    /// <summary>The plan (false) or the 3D view (true).</summary>
    public void ShowView(bool threeD)
    {
        _threeD = threeD;
        _planBox.IsVisible = !threeD;
        _box3d.IsVisible = threeD;
        _planButton.IsChecked = !threeD;
        _view3dButton.IsChecked = threeD;
        if (threeD && _viewStale) Show3D();
        (threeD ? (Control)_view : _plan).Focus();
    }

    // ---- the right-hand side ----

    Control SidePanel()
    {
        _pickedTab = Tab("Picked", PickedTab());
        _townTab = Tab("Town", TownTab());
        _mapTab = Tab("Map", MapTab());
        _makePage = TaskPage("Town maker", "A whole town the size below, in the map's look: roads, a market square, districts, buildings of the right kind for each (your saved buildings where they suit a plot), fields, trees and street names. Press it again for another; undo puts your map back.", MakeTownPage());
        _readPage = TaskPage("Battle map", "Read a town from a top-down battle map picture: its grid is found by itself, then every square is read as ground, buildings or trees. It's a best guess: change whatever it gets wrong.", PictureTab());
        _savePage = TaskPage("Picture", "The map drawn from above as a battle map, for a virtual table or to print: textured ground, roofs, trees, walls, the grid. PNG, JPEG or WebP.", SavePage());
        _cutPage = TaskPage("Cut", "The town's pieces in the design, each set nested onto the bed, a page per bed: the buildings, the ground as tiles, bridges, town walls, trees and scenery.", CutTab());
        foreach (var t in new[] { _pickedTab, _townTab, _mapTab, _makePage, _readPage, _savePage, _cutPage }) _side.Items.Add(t);
        _lastTab = _pickedTab;
        _side.SelectionChanged += (_, e) =>
        {
            if (e.Source != _side) return;
            if (_side.SelectedItem is TabItem t && !_taskButtons.ContainsKey(t)) _lastTab = t;
            foreach (var (p, b) in _taskButtons) b.IsChecked = _side.SelectedItem == p;
            if (_side.SelectedItem == _cutPage) RefreshCut();
            if (_side.SelectedItem == _savePage) RefreshPictureInfo();
        };
        _side.Margin = new(10, 4, 10, 0);
        return new Border { Background = Brush("Bg1"), BorderBrush = Brush("Line"), BorderThickness = new(1, 0, 0, 0), Child = _side };
    }

    TabItem TaskPage(string header, string about, Control content)
    {
        var close = new Button
        {
            Classes = { "icon" }, Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Data = IconOrPath(CloseIcon), Width = 14, Height = 14 },
        };
        ToolTip.SetTip(close, "Close it (back to the tabs)");
        Avalonia.Automation.AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => ClosePage();
        var title = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 10, 10, 0) };
        title.Children.Add(new TextBlock { Text = header, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(close.With(c => Grid.SetColumn(c, 1)));
        var body = new StackPanel { Spacing = 10, Margin = new(0, 6, 10, 10), Children = { Hint(about), content } };
        var page = new Grid { RowDefinitions = new("Auto,*") };
        page.Children.Add(title);
        page.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body }.With(c => Grid.SetRow(c, 1)));
        return new TabItem { Header = header, IsVisible = false, Content = page };
    }

    void ClosePage() => _side.SelectedItem = _lastTab;

    /// <summary>
    /// Shows one of the right-hand side's tabs (Picked, Town, Map) or the toolbar's pages (Town maker, Battle map,
    /// Picture, Cut) by its name: how the help's pictures open them.
    /// </summary>
    public void ShowPage(string name)
    {
        if (_side.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(t.Header as string, name, StringComparison.OrdinalIgnoreCase)) is { } t) _side.SelectedItem = t;
    }

    bool OnPage(TabItem page) => _side.SelectedItem == page;

    // ---- along the bottom ----

    Control StatusBar()
    {
        _summary.TextWrapping = TextWrapping.NoWrap;
        _summary.TextTrimming = TextTrimming.CharacterEllipsis;
        _summary.Classes.Add("muted");
        _summary.MaxWidth = 640;
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxLines = 2;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.Margin = new(12, 0);
        _status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) ToolTip.SetTip(_status, string.IsNullOrEmpty(_status.Text) ? null : _status.Text); };
        _summary.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) ToolTip.SetTip(_summary, _summary.Text); };
        _plan.Hovered += p => _where.Text = p is { } at && _map.In((int)Math.Floor(at.X), (int)Math.Floor(at.Y))
            ? $"Square {(int)Math.Floor(at.X) + 1}, {(int)Math.Floor(at.Y) + 1} ({at.X:0.0}, {at.Y:0.0})"
            : "";
        var bar = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new(12, 5) };
        bar.Children.Add(_where);
        bar.Children.Add(_status.With(c => Grid.SetColumn(c, 1)));
        bar.Children.Add(_summary.With(c => Grid.SetColumn(c, 2)));
        return new Border { Background = Brush("Bg0"), BorderBrush = Brush("Line"), BorderThickness = new(0, 1, 0, 0), MinHeight = 30, Child = bar };
    }

    // ---- the pages ----

    Control MapTab()
    {
        _name.Watermark = "The town's name";
        ToolTip.SetTip(_resize, "Squares added at the right and bottom are grass; buildings that would hang off the edge are taken away.");
        return Stack(
            Explained("The map", "A grid of squares, as big as the town needs: every building stands on whole squares.",
                Field("Name", _name),
                Field("Look", _theme, tip: "The theme new buildings are made in (walls, roofs, doors and windows)."),
                Field("Squares across", _across),
                Field("Squares down", _down),
                Field("Square (mm)", _square, tip: "One square of the grid on the table: 25.4 mm is an inch, the usual battle map square."),
                _resize),
            Explained("Maps", "Your maps are kept in the city builder's folder, and the last one opens again next time. New, Open and Save as are at the left of the toolbar.",
                _sample, _mapInfo));
    }

    Control MakeTownPage()
    {
        ToolTip.SetTip(_tMake, "A new map with a whole town on it. Undo puts the old one back.");
        return Stack(
            Explained("The town", "How big, how its streets run and what's in it.",
                Field("Squares across", _tAcross, tip: "Up to 300 each way, made in a few seconds."),
                Field("Squares down", _tDown),
                Field("Streets", _tLayout),
                Field("How full", _tDensity, tip: "Left, a house here and there; right, packed side by side."),
                _tWalls, _tRiver, _tSquare, _tSaved),
            _tMake, _tNotes);
    }

    Control SavePage() => Stack(
        Explained("The picture", "How big, and what's drawn over the map.",
            Field("Pixels per square", _pPx, tip: "70 to 140 suits most virtual tables; to print, 100 pixels to a square printed an inch across is 100 dots to the inch."),
            _pGrid, _pLabels, _pNames, _pBox, _pInfo),
        _pSave);

    Control PictureTab()
    {
        ToolTip.SetTip(_read, "Makes a new map from the picture: the ground of every square, the buildings and the trees. Undo puts the old one back.");
        ToolTip.SetTip(_sensitivity, "Lower finds more roofs (and more mistakes); higher, only the clearest.");
        return Stack(
            Explained("A battle map picture", "A top-down map of a town or village. Its grid is found by itself (or type how many squares across it is).",
                new WrapPanel { Children = { _openPic.With(b => b.Margin = new(0, 0, 4, 4)), _usePic.With(b => b.Margin = new(0, 0, 4, 4)), _samplePic.With(b => b.Margin = new(0, 0, 4, 4)) } },
                _picNote,
                Field("Square (pixels)", _px),
                Field("Grid starts at x", _offX),
                Field("Grid starts at y", _offY),
                Field("Squares across", _picAcross, tip: "For a picture with no grid lines: how many squares across it is."),
                _findGrid),
            Explained("Reading it", "What to look for. It's a best guess: change whatever it gets wrong.",
                Field("Buildings are", _shows),
                _findBuildings,
                Field("Roofs", _sensitivity, tip: "Lower finds more roofs (and more mistakes); higher, only the clearest."),
                Field("Biggest (squares)", _largest, tip: "Anything bigger than this is ground (a square, a field), not a building."),
                _findTrees,
                _usePrefabs,
                _read,
                _readNotes),
            Explained("Seeing it", "The picture under the plan, to check the map against it.", _showPic, Field("Ground colours", _opacity, tip: "How strongly the ground read from the picture is painted over it.")));
    }

    Control CutTab()
    {
        ToolTip.SetTip(_cTile, "The biggest tile, in squares each way: each must fit on the bed.");
        ToolTip.SetTip(_makeAll, "Every building's pieces, one after the other, each nested onto its own beds (pages). A big town is a lot of pieces.");
        return Stack(
            _cBox,
            Explained("The buildings", "Each building's walls, floors and roofs, made the building editor's way, each on pages of its own. A big town is a great many pieces: pick a few buildings and press Put the picked buildings in the design on the Picked tab instead.",
                _makeAll),
            Explained("The ground", "The map's ground as floor tiles that lock together: cobbles etched on the roads, flagstones on paving, grass, dirt, water and fields each their own way. Squares under the buildings are left plain (each building stands on its own floor).",
                Field("Tile size (squares)", _cTile), Field("Sheet (mm)", _cThick), Field("Edges", _cJoin), _cGrid, _cOutlines, _cGroundInfo, _cGround),
            Explained("Bridges", "A bridge wherever a road crosses water, from the bridge maker: stone arches for a cobbled road, planks for a track, as wide as the road and reaching onto the banks.",
                _cBridgesInfo, _cBridges),
            Explained("Town walls", "The furniture maker's town wall in the map's look: sections two squares and one long, corners, a gatehouse over each road through it, a turret at each end, and the guard towers along it.",
                _cWallsInfo, _cWalls),
            Explained("Trees", "The furniture maker's trees in the map's look (oaks and pines, dead trees, palms or giant mushrooms), each as big as on the map. Bushes aren't cut.",
                _cTreesInfo, _cTrees),
            Explained("Furniture", "The furniture maker's pieces put down with the Furniture tool, in the map's look, the same pieces counted together and nested onto the bed.",
                _cFurnitureInfo, _cFurniture),
            Explained("Scenery", "The scenery maker's wells, carts, crates (as barrels), boulders and fences, and the furniture maker's horse troughs, stages, market stalls, outdoor bars, notice boards, fountains, gallows, street lamps and statues.",
                _cSceneryInfo, _cScenery));
    }

    Control TownTab()
    {
        _townList.SelectionMode = SelectionMode.Single;
        return Stack(
            Explained("The buildings", "Every building on the map. Click one to pick it and go to it on the plan.",
                _find, Field("Type", _findType), Field("District", _findDistrict), _townList, _townCounts),
            Explained("Districts", "The part of town each building is in: set it on the Picked tab, for several buildings at once too.", _byDistrict, _legend));
    }

    Control PickedTab()
    {
        var none = Hint("Click a building, road, wall, tree, piece of furniture or label on the plan with Select, or drag a box round several, to change them here.");
        var building = BuildingTab();
        _groupBuildings = Stack(Field("Type", _gType, tip: "Typed here, every building picked gets it."), Field("District", _gDistrict, tip: "Typed here, every building picked gets it."));
        _groupProps = Stack(Field("Tree or thing", _gProp), Field("Size", _gPropSize, tip: "1 is about a square across."));
        _groupPanel = Stack(
            Explained("What's picked", "Drag any of it on the plan to move it all; Ctrl+C and Ctrl+V copy and paste it (where the pointer is), R turns it, Delete takes it away.",
                _gInfo, _groupBuildings, _groupProps,
                new WrapPanel { Children = { _gTurn.With(b => b.Margin = new(0, 0, 4, 4)), _gCopy.With(b => b.Margin = new(0, 0, 4, 4)), _gDelete.With(b => b.Margin = new(0, 0, 4, 4)) } },
                _gMake));
        _labelPanel = Stack(
            Explained("The label", "Drag it on the plan to move it; R turns it.", Field("Text", _lText), Field("Size (squares)", _lSize), Field("Turn (degrees)", _lTurn), _lDelete));
        _roadPanel = Stack(
            Explained("The road", "Its name is written along it. Drag it to move it; drag a round handle to move a bend or an end, a diamond to move a whole stretch. Double-click it to add a bend, right-click a bend to take it out.",
                Field("Name", _rName), Field("Surface", _rSurface), Field("Width (squares)", _rWidth, tip: "2 for a street, 4 for a high road."), _rInfo, _rDelete));
        _fencePanel = Stack(
            Explained("The wall", "Drag it to move it; drag a round handle to move a corner or an end, a diamond to move a whole side. Double-click it to add a corner, right-click a corner to take it out. Where a road goes through it there's a gate.",
                Field("Kind", _wKind), _wRuined, _wInfo, _wDelete));
        _itemPanel = Stack(
            Explained("The furniture", "Drag it on the plan to move it on the fine grid; the arrow keys move it a tenth of a square. R turns it a quarter turn, Ctrl+R 15°. It's cut with the rest of the furniture on the Cut page.",
                Field("Name", _iName), Field("Turn (degrees)", _iTurn), _iInfo,
                new WrapPanel { Children = { _iCopy.With(b => b.Margin = new(0, 0, 4, 4)), _iDelete.With(b => b.Margin = new(0, 0, 4, 4)) } }));
        _noBuilding = none;
        return new StackPanel { Children = { none, building, _groupPanel, _itemPanel, _labelPanel, _roadPanel, _fencePanel } };
    }

    Control BuildingTab()
    {
        _bTags.Watermark = "Words to find it by, with commas between";
        _bDistrict.Watermark = "Residential, market, docks…";
        _buildingPanel = Stack(
            Explained("The building", "What it is, for finding it again; its name shows on the plan.",
                Field("Name", _bName), Field("Type", _bType), Field("District", _bDistrict), Field("Tags", _bTags), _bInfo, _bBuilt),
            Explained("Its shape", "Quick changes here; open it in the building editor for everything else (rooms, doors, furniture, stairs).",
                new WrapPanel { Children = { _bTurn.With(b => b.Margin = new(0, 0, 4, 4)), _bUp.With(b => b.Margin = new(0, 0, 4, 4)), _bDownLevel.With(b => b.Margin = new(0, 0, 4, 4)) } },
                Field("Tilt (°)", _bTilt, tip: "A building read off a map that sits at an angle keeps its angle: clockwise degrees past its quarter turns. Square it to the grid to put it on whole squares."),
                _bSquare,
                Field("Roof", _bRoof), Field("Covering", _bCovering), _bRoofIt,
                _bEdit),
            Explained("More", "Copy it, take it away, keep it to use again, or cut it.", new WrapPanel { Children = { _bCopy.With(b => b.Margin = new(0, 0, 4, 4)), _bDelete.With(b => b.Margin = new(0, 0, 4, 4)), _bSavePrefab.With(b => b.Margin = new(0, 0, 4, 4)) } },
                _makeOne));
        return _buildingPanel;
    }

    static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 10, 10), Children = { content } } },
    };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = items.Length > 0 ? 0 : -1 };

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }
}
