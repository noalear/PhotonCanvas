using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// One piece of furniture's settings: its kind's name and what it is, how many, its style and sizes,
/// and every choice its kind has (legs, backs, fronts, lids, beds, doors...). Shows only the rows that
/// suit the kind. Used by the furniture maker for the piece picked on its list, and by other makers
/// that stand the furniture maker's pieces about (the building editor).
/// </summary>
public sealed class ItemEditor : StackPanel
{
    FurnitureItem _item = Defaults.For(ItemKind.Table);
    bool _loading;
    readonly Control _countRow;

    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold }, _about = Hint();
    readonly NumericUpDown _count = Number(1, 500, 1, "0");
    readonly NumericUpDown _w = Number(1, 1000, 0.5m, "0.#"), _d = Number(0, 1000, 0.5m, "0.#"), _h = Number(1, 1000, 0.5m, "0.#");
    readonly TextBlock _wLabel = Label("Width"), _dLabel = Label("Depth"), _hLabel = Label("Height");
    Control _dRow = null!;
    readonly ComboBox _legs = Combo("Four legs", "Solid ends", "Trestles", "Round, crossed legs");
    readonly CheckBox _rail = new() { Content = "A rail low down between the legs" };
    readonly NumericUpDown _legW = Number(0, 50, 0.5m, "0.#");
    readonly ComboBox _top = Combo("Round", "Square");
    readonly NumericUpDown _seat = Number(2, 500, 0.5m, "0.#");
    readonly ComboBox _back = Combo("Solid", "Upright slats", "Ladder (rails across)");
    readonly NumericUpDown _shelves = Number(0, 30, 1, "0");
    readonly CheckBox _backPanel = new() { Content = "A back panel" }, _books = new() { Content = "Books etched on the back" };
    readonly ComboBox _lid = Combo();
    readonly CheckBox _bands = new();
    readonly TextBox _label = new() { Watermark = "Picked to suit", MaxLength = 24 };
    Control _labelRow = null!;
    readonly NumericUpDown _foot = Number(1, 500, 0.5m, "0.#"), _mattress = Number(2, 500, 0.5m, "0.#");
    readonly CheckBox _bedding = new() { Content = "A pillow and a blanket etched on" };
    readonly ComboBox _doorShape = Combo("Square top", "Round arch", "Pointed arch"), _hinge = Combo("On the left", "On the right");
    readonly NumericUpDown _wallT = Number(0.5m, 50, 0.5m, "0.##"), _wallSheet = Number(0.5m, 20, 0.5m, "0.##"), _overlap = Number(0, 10, 0.5m, "0.#"), _slot = Number(2, 30, 0.5m, "0.#");
    Control _legRows = null!, _topRow = null!, _seatRow = null!, _backRow = null!, _shelvesRow = null!, _shelfRows = null!, _boxRows = null!, _bedRows = null!, _doorRows = null!, _shelfWallRows = null!, _wallRows = null!, _legWRow = null!;
    readonly TextBlock _hLabelRow = Label("");
    Control _hRow = null!;
    // The newer kinds' options: a style (front, kind of seat, pattern...) and the sizes and choices that go with it.
    readonly TextBlock _styleLabel = Label("Style");
    readonly ComboBox _style = Combo();
    readonly ComboBox _crown = Combo("Flat", "Cornice (two layers)", "Pediment", "Arch");
    readonly ComboBox _contents = Combo("Books", "Bottles", "Jars", "Plates", "Scrolls", "Machines", "Nothing");
    readonly ComboBox _backTop = Combo("Square", "Rounded", "Arched", "Pointed", "Winged");
    readonly NumericUpDown _cols = Number(1, 12, 1, "0"), _rows = Number(1, 12, 1, "0"), _arm = Number(2, 500, 0.5m, "0.#"), _hang = Number(0, 500, 0.5m, "0.#");
    readonly TextBlock _colsLabel = Label("Across"), _rowsLabel = Label("Rows");
    readonly ComboBox _look = Combo(["The list's theme", .. FurnitureThemes.Names]);
    Control _styleRow = null!, _crownRow = null!, _contentsRow = null!, _backTopRow = null!, _colsRow = null!, _rowsRow = null!, _armRow = null!, _hangRow = null!, _lookRow = null!;


    /// <summary>A setting was changed: the piece as it was, and as it is now.</summary>
    public event Action<FurnitureItem, FurnitureItem>? Changed;

    /// <summary>The piece as the settings say.</summary>
    public FurnitureItem Item => _item;

    /// <param name="wallExtra">Shown with a door's or wall shelf's wall settings (the furniture maker's "Match the walls").</param>
    /// <param name="count">Show "How many" (the building editor stands each piece on its own).</param>
    public ItemEditor(Control? wallExtra = null, bool count = true)
    {
        Spacing = 8;
        _legWRow = Field("Leg width", _legW, tip: "How wide the legs are (mm). 0 = to suit the sheet.");
        _legRows = Stack(Field("Legs", _legs), _rail);
        _topRow = Field("Seat shape", _top);
        _seatRow = Field("Seat height", _seat, tip: "How high the top of the seat is (mm).");
        _backRow = Field("Back", _back);
        _shelvesRow = Field("Shelves", _shelves, tip: "Shelves between the bottom one and the top.");
        _shelfRows = Stack(_backPanel, _books);
        _styleRow = Line(_styleLabel, _style);
        _crownRow = Field("Top", _crown, tip: "What's on top: a flat top, a stepped cornice, a pointed pediment or an arch.");
        _contentsRow = Field("On the shelves", _contents, tip: "What's etched on the back between the shelves, where it shows through open shelves or glass.");
        _backTopRow = Field("Back's top", _backTop);
        _armRow = Field("Arm height", _arm, tip: "How high the arms are (mm).");
        _colsRow = Line(_colsLabel, _cols);
        _rowsRow = Line(_rowsLabel, _rows);
        _hangRow = Field("Hangs at", _hang, tip: "How high its bottom edge is up the wall (mm).");
        _lookRow = Field("Look", _look, tip: "The theme this piece is made in: its doors, handles and ornament. Ready-made pieces keep their own, so a sci-fi console can stand in a medieval list.");
        _labelRow = Field("Stencilled", _label, tip: "The words painted on a cargo or military crate's long sides. Left empty, each crate gets some to suit (SUPPLIES, FRAGILE, AMMO, RATIONS…).");
        _boxRows = Stack(Field("Lid", _lid), _bands, _labelRow);
        _bedRows = Stack(Field("Foot board", _foot, tip: "How high the foot board is (mm)."), Field("Mattress", _mattress, tip: "How high the top of the mattress is (mm)."), _bedding);
        _doorRows = Stack(Field("Doorway", _doorShape, tip: "The doorway's shape, as cut by Walls."), Field("Hinges", _hinge, tip: "Which side of the doorway the door hangs on, seen from the side it opens to."),
            Field("Covers by", _overlap, tip: "How far the door reaches over the wall round the doorway (mm)."));
        _shelfWallRows = Stack(Field("Wall's sheet", _wallSheet, tip: "The sheet the wall was cut from (mm): its mount slots are that wide."), Field("Slot height", _slot, tip: "How tall the wall's mount slots are (mm)."));
        _wallRows = Stack(Field("Wall thickness", _wallT, tip: "How thick the wall is (mm): both panels if it's two-sided."), _shelfWallRows);
        if (wallExtra is not null) ((StackPanel)_wallRows).Children.Add(wallExtra);
        _dRow = Line(_dLabel, _d);
        _hRow = Line(_hLabel, _h);
        _countRow = Field("How many", _count);
        _countRow.IsVisible = count;
        foreach (var c in new Control[] { _title, _about, _countRow, _styleRow, Line(_wLabel, _w), _dRow, _hRow,
                     _legRows, _topRow, _legWRow, _seatRow, _armRow, _backRow, _backTopRow, _crownRow, _colsRow, _rowsRow, _shelvesRow, _contentsRow, _shelfRows, _boxRows, _bedRows, _hangRow, _doorRows, _wallRows, _lookRow })
            Children.Add(c);

        foreach (var n in new[] { _count, _w, _d, _h, _legW, _seat, _shelves, _foot, _mattress, _wallT, _wallSheet, _overlap, _slot, _cols, _rows, _arm, _hang })
            n.ValueChanged += (_, _) => Edited();
        _style.SelectionChanged += (_, _) => { if (!_loading) { Edited(); Load(); } };
        foreach (var c in new[] { _legs, _top, _back, _lid, _doorShape, _hinge, _crown, _contents, _backTop, _look })
            c.SelectionChanged += (_, _) => Edited();
        foreach (var c in new[] { _rail, _backPanel, _books, _bands, _bedding })
            c.IsCheckedChanged += (_, _) => Edited();
        _label.TextChanged += (_, _) => Edited();
    }

    /// <summary>Shows a piece's settings (nothing is raised).</summary>
    public void Show(FurnitureItem item)
    {
        _item = item;
        Load();
    }

    void Edited()
    {
        if (_loading) return;
        var before = _item;
        _item = FromControls();
        if (_item != before) Changed?.Invoke(before, _item);
    }

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = items.Length > 0 ? 0 : -1 };

    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };

    /// <summary>Like <see cref="PhotonCanvas.App.UiKit.Field"/>, with a label that can change.</summary>
    static Control Line(TextBlock label, Control editor, string? tip = null)
    {
        var grid = new Grid { ColumnDefinitions = new("110,*") };
        grid.Children.Add(label);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (tip is not null) { ToolTip.SetTip(label, tip); ToolTip.SetTip(editor, tip); }
        return grid;
    }

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void Load()
    {
        var it = _item;
        var kind = it.Kind;
        bool was = _loading;
        _loading = true;
        var info = Defaults.Info(kind);
        _title.Text = string.IsNullOrWhiteSpace(it.Name) ? info.Name : $"{it.Name} ({info.Name.ToLowerInvariant()})";
        _about.Text = info.About;
        _count.Value = Math.Max(1, it.Count);
        _w.Value = D(it.WidthMm); _d.Value = D(it.DepthMm); _h.Value = D(it.HeightMm);
        _legs.SelectedIndex = (int)it.Legs; _rail.IsChecked = it.Stretcher; _legW.Value = D(it.LegMm);
        _top.SelectedIndex = it.Top == TopShape.Round ? 0 : 1;
        _seat.Value = D(it.SeatMm); _back.SelectedIndex = (int)it.Back;
        _shelves.Value = it.Shelves; _backPanel.IsChecked = it.BackPanel; _books.IsChecked = it.Books;
        _lid.ItemsSource = kind == ItemKind.Crate ? new List<string> { "Closed (glued on)", "Open top" } : new List<string> { "Lifts off (a rim under it)", "Glued on", "None (open)", "Domed, lifts off" };
        _lid.SelectedIndex = kind == ItemKind.Crate ? (it.Lid == LidStyle.None ? 1 : 0) : (int)it.Lid;
        _bands.Content = kind == ItemKind.Crate ? "Boards round the edge and a brace etched on" : "Iron bands and a lock plate etched on";
        _bands.IsChecked = it.Bands;
        _bands.IsVisible = kind != ItemKind.Crate;
        _label.Text = it.Label ?? "";
        _labelRow.IsVisible = kind == ItemKind.Crate && it.Bands && it.CrateStyle is (CrateStyle.Cargo or CrateStyle.Military);
        _foot.Value = D(it.FootMm); _mattress.Value = D(it.MattressMm); _bedding.IsChecked = it.Bedding;
        _doorShape.SelectedIndex = (int)it.Shape; _hinge.SelectedIndex = (int)it.Hinge; _overlap.Value = D(it.OverlapMm);
        _wallT.Value = D(it.WallMm); _wallSheet.Value = D(it.WallSheetMm); _slot.Value = D(it.MountSlotMm);
        var styles = StylesOf(kind);
        _styleRow.IsVisible = styles is not null;
        if (styles is { } st)
        {
            _styleLabel.Text = st.Label;
            _style.ItemsSource = st.Names.ToList();
            _style.SelectedIndex = Math.Clamp(StyleOf(it), 0, st.Names.Length - 1);
        }
        _crown.SelectedIndex = (int)it.Crown; _contents.SelectedIndex = (int)it.Contents; _backTop.SelectedIndex = (int)it.BackTop;
        _cols.Value = Math.Max(1, it.Columns); _rows.Value = Math.Max(1, it.Rows); _arm.Value = D(it.ArmMm); _hang.Value = D(it.HangMm);
        int lookAt = it.Theme is { } th ? FurnitureThemes.Names.ToList().IndexOf(th) : -1;
        _look.SelectedIndex = lookAt + 1;

        (string W, string? D, string H) labels = kind switch
        {
            ItemKind.Table when it.Legs == LegStyle.Round => ("Across", null, "Height"),
            ItemKind.Table or ItemKind.Bench => ("Length", "Depth", "Height"),
            ItemKind.Stool => ("Across", null, "Height"),
            ItemKind.Chair => ("Seat width", "Seat depth", "Back height"),
            ItemKind.Bookshelf or ItemKind.Chest or ItemKind.Crate => ("Width", "Depth", "Height"),
            ItemKind.Bed => ("Length", "Width", "Head board"),
            ItemKind.WallDoor => ("Doorway width", null, "Doorway height"),
            ItemKind.WallShelf => ("Length", "Depth", "Bracket size"),
            ItemKind.Armchair => ("Width", "Depth", "Back height"),
            ItemKind.Coffin => ("Length", "Width", "Height"),
            ItemKind.Rug => (it.Rug == RugPattern.Round ? "Across" : "Length", it.Rug == RugPattern.Round ? null : "Width", ""),
            ItemKind.WallHanging => ("Width", null, "Height"),
            ItemKind.Candlestand => ("Spread", "Foot", "Height"),
            _ => ("Width", "Depth", "Height"),
        };
        _wLabel.Text = labels.W;
        _hLabel.Text = labels.H;
        _hRow.IsVisible = labels.H != "";
        _dLabel.Text = labels.D ?? "";
        _dRow.IsVisible = labels.D is not null;
        _legRows.IsVisible = kind is ItemKind.Table or ItemKind.Bench;
        _legWRow.IsVisible = kind is ItemKind.Table or ItemKind.Bench or ItemKind.Chair or ItemKind.Bed;
        _topRow.IsVisible = kind == ItemKind.Stool;
        _seatRow.IsVisible = kind is ItemKind.Chair or ItemKind.Armchair;
        _backRow.IsVisible = kind == ItemKind.Chair;
        _armRow.IsVisible = _backTopRow.IsVisible = kind == ItemKind.Armchair && it.Seat != SeatStyle.Pew;
        _crownRow.IsVisible = kind == ItemKind.Cabinet;
        _contentsRow.IsVisible = kind == ItemKind.Cabinet && it.Front is CabinetFront.Open or CabinetFront.Glass or CabinetFront.Hutch;
        _shelvesRow.IsVisible = kind == ItemKind.Bookshelf || kind == ItemKind.Cabinet && it.Front is not (CabinetFront.Clock or CabinetFront.Altar or CabinetFront.Drawers);
        (string? cols, string? rows) counts = kind switch
        {
            ItemKind.Cabinet => (it.Front is CabinetFront.Clock or CabinetFront.Altar or CabinetFront.Open ? null : "Across", it.Front is CabinetFront.Drawers or CabinetFront.DrawersOverDoors ? "Drawer rows" : null),
            ItemKind.Armchair when it.Seat is SeatStyle.Sofa or SeatStyle.Pew => ("Seats", null),
            ItemKind.Screen => ("Panels", null),
            ItemKind.WeaponRack => (null, "Weapons"),
            _ => (null, null),
        };
        _colsLabel.Text = counts.cols ?? ""; _colsRow.IsVisible = counts.cols is not null;
        _rowsLabel.Text = counts.rows ?? ""; _rowsRow.IsVisible = counts.rows is not null;
        _hangRow.IsVisible = kind == ItemKind.WallHanging;
        _lookRow.IsVisible = (int)kind >= (int)ItemKind.Cabinet;
        _shelfRows.IsVisible = kind == ItemKind.Bookshelf;
        _boxRows.IsVisible = kind is ItemKind.Chest or ItemKind.Crate;
        _bedRows.IsVisible = kind == ItemKind.Bed;
        _doorRows.IsVisible = kind == ItemKind.WallDoor;
        _shelfWallRows.IsVisible = kind == ItemKind.WallShelf;
        _wallRows.IsVisible = kind is ItemKind.WallDoor or ItemKind.WallShelf;
        _loading = was;
    }

    /// <summary>The chosen piece of furniture as the settings say.</summary>
    FurnitureItem FromControls()
    {
        var it = _item;
        var kind = it.Kind;
        return it with
        {
            Count = _countRow.IsVisible ? (int)V(_count, 1) : it.Count,
            WidthMm = V(_w, it.WidthMm), DepthMm = V(_d, it.DepthMm), HeightMm = V(_h, it.HeightMm),
            Legs = (LegStyle)Math.Max(0, _legs.SelectedIndex), Stretcher = _rail.IsChecked == true, LegMm = V(_legW, 0),
            Top = _top.SelectedIndex == 1 ? TopShape.Square : TopShape.Round,
            SeatMm = V(_seat, it.SeatMm), Back = (BackStyle)Math.Max(0, _back.SelectedIndex),
            Shelves = (int)V(_shelves, it.Shelves), BackPanel = _backPanel.IsChecked == true, Books = _books.IsChecked == true,
            Lid = kind == ItemKind.Crate ? (_lid.SelectedIndex == 1 ? LidStyle.None : LidStyle.Flat) : (LidStyle)Math.Max(0, _lid.SelectedIndex),
            Bands = _bands.IsChecked == true,
            Label = string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(),
            FootMm = V(_foot, it.FootMm), MattressMm = V(_mattress, it.MattressMm), Bedding = _bedding.IsChecked == true,
            Shape = (DoorShape)Math.Max(0, _doorShape.SelectedIndex), Hinge = (HingeSide)Math.Max(0, _hinge.SelectedIndex), OverlapMm = V(_overlap, it.OverlapMm),
            WallMm = V(_wallT, it.WallMm), WallSheetMm = V(_wallSheet, it.WallSheetMm), MountSlotMm = V(_slot, it.MountSlotMm),
            Crown = (Crown)Math.Max(0, _crown.SelectedIndex), Contents = (ShelfContents)Math.Max(0, _contents.SelectedIndex), BackTop = (BackTop)Math.Max(0, _backTop.SelectedIndex),
            Columns = (int)V(_cols, it.Columns), Rows = (int)V(_rows, it.Rows), ArmMm = V(_arm, it.ArmMm), HangMm = V(_hang, it.HangMm),
            Theme = _look.SelectedIndex > 0 ? FurnitureThemes.Names[_look.SelectedIndex - 1] : null,
        } is var made && StylesOf(kind) is not null && _style.SelectedIndex >= 0 ? WithStyle(made, _style.SelectedIndex) : made;
    }

    // ---- the newer kinds' styles ----

    public static (string Label, string[] Names)? StylesOf(ItemKind k) => k switch
    {
        ItemKind.Cabinet => ("Front", ["Doors", "Drawers", "Drawers over doors", "Glass doors", "Open shelves", "Dresser (shelves over a cupboard)", "Panelled doors", "Lockers", "Machine panels", "Clock", "Lattice doors", "Altar"]),
        ItemKind.Armchair => ("Kind", ["Armchair", "Throne on a dais", "Sofa or settle", "Pew", "Command chair"]),
        ItemKind.Bed => ("Kind", ["Plain", "Four-poster", "Bunk beds", "Pallet on sleepers"]),
        ItemKind.Fireplace => ("Kind", ["Fireplace", "Bread oven", "Forge"]),
        ItemKind.SlantDesk => ("Kind", ["Lectern", "Writing desk (scriptorium)", "Control console", "Map table"]),
        ItemKind.Stand => ("Kind", ["Armour", "Training dummy", "Coat stand"]),
        ItemKind.Coffin => ("Kind", ["Coffin", "Sarcophagus", "Sleeper pod"]),
        ItemKind.Rug => ("Pattern", ["Bordered", "Kilim", "Damask", "Hide", "Deck mat", "Round"]),
        ItemKind.WallHanging => ("Kind", ["Tapestry", "Banner", "Painting", "Shield and swords", "Map", "Screen"]),
        ItemKind.Candlestand => ("Kind", ["Candelabra", "Lamp", "Torch"]),
        ItemKind.Screen => ("Kind", ["Lattice", "Tracery", "Plain panels", "Barrier"]),
        ItemKind.Desk => ("On top", ["Papers, a book and ink", "A screen and keyboard"]),
        ItemKind.Crate => ("Boards", Crates.StyleNames),
        _ => null,
    };

    public static int StyleOf(FurnitureItem it) => it.Kind switch
    {
        ItemKind.Cabinet => (int)it.Front,
        ItemKind.Armchair => (int)it.Seat,
        ItemKind.Bed => (int)it.BedStyle,
        ItemKind.Fireplace => (int)it.Hearth,
        ItemKind.SlantDesk => (int)it.Slant,
        ItemKind.Stand => (int)it.Stand,
        ItemKind.Coffin => (int)it.Coffin,
        ItemKind.Rug => (int)it.Rug,
        ItemKind.WallHanging => (int)it.Hanging,
        ItemKind.Candlestand => (int)it.Candle,
        ItemKind.Screen => (int)it.ScreenStyle,
        ItemKind.Desk => it.Contents == ShelfContents.Tech ? 1 : 0,
        ItemKind.Crate => it.Bands ? (int)it.CrateStyle + 1 : 0,
        _ => 0,
    };

    public static FurnitureItem WithStyle(FurnitureItem it, int i) => it.Kind switch
    {
        ItemKind.Cabinet => it with { Front = (CabinetFront)i },
        ItemKind.Armchair => it with { Seat = (SeatStyle)i },
        ItemKind.Bed => it with { BedStyle = (BedStyle)i },
        ItemKind.Fireplace => it with { Hearth = (HearthStyle)i },
        ItemKind.SlantDesk => it with { Slant = (SlantStyle)i },
        ItemKind.Stand => it with { Stand = (StandStyle)i },
        ItemKind.Coffin => it with { Coffin = (CoffinStyle)i },
        ItemKind.Rug => it with { Rug = (RugPattern)i },
        ItemKind.WallHanging => it with { Hanging = (HangingStyle)i },
        ItemKind.Candlestand => it with { Candle = (CandleStyle)i },
        ItemKind.Screen => it with { ScreenStyle = (ScreenStyle)i },
        ItemKind.Desk => it with { Contents = i == 1 ? ShelfContents.Tech : ShelfContents.Books },
        ItemKind.Crate => it with { Bands = i > 0, CrateStyle = (CrateStyle)Math.Max(0, i - 1) },
        _ => it,
    };
}
