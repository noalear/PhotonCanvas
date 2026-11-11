using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.DeviceArray;

/// <summary>
/// Make or change a device array, seen live on the canvas: how many devices across and down (or as
/// many as fit an area), their pitch, where the first one is, the counting order, and which are off.
/// </summary>
public sealed class DeviceArrayWindow : Window
{
    static readonly string[] OrderNames =
    [
        "Across, row by row",
        "Down, column by column",
        "Across, back and forth",
        "Down, back and forth",
    ];

    readonly PhotonCanvas.Doc.Document _doc;
    readonly DocState _base;
    readonly Guid? _editing;
    readonly HashSet<Guid> _ids;
    readonly Rect2 _firstBox;
    readonly ComboBox _sizeBy = new() { ItemsSource = new[] { "Columns and rows", "An area" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _cols = Number(1, ArraySpec.MaxSide, 1, "0"), _rows = Number(1, ArraySpec.MaxSide, 1, "0");
    readonly NumericUpDown _areaW = Number(1, 2000, 1, "0.##"), _areaH = Number(1, 2000, 1, "0.##");
    readonly NumericUpDown _pitchX = Number(0, 2000, 0.5m, "0.###"), _pitchY = Number(0, 2000, 0.5m, "0.###");
    readonly NumericUpDown _x = Number(-2000, 2000, 1, "0.###"), _y = Number(-2000, 2000, 1, "0.###");
    readonly ComboBox _order = new() { ItemsSource = OrderNames, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox _keep = new() { Content = "Devices that are off keep their number" };
    readonly Control _countFields, _areaFields;
    readonly CellGrid _grid = new();
    readonly TextBlock _info = Hint(), _warn = new() { Foreground = Brush("Warn"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
    ArraySpec _spec;
    bool _loading = true, _done;

    /// <summary>The array made or changed (null when cancelled, released or removed).</summary>
    public Guid? Result { get; private set; }

    /// <summary>Make an array of the selected shapes, or (with <paramref name="editing"/>) change that one.</summary>
    public DeviceArrayWindow(PhotonCanvas.Doc.Document doc, MachineProfile machine, Guid? editing)
    {
        _doc = doc;
        _base = doc.State;
        _editing = editing;
        Title = editing is null ? "Device array" : "Change device array";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        if (editing is { } id && DeviceArrays.Find(_base, id) is { } view)
        {
            _ids = [.. view.Firsts.Select(f => f.Id)];
            _firstBox = view.FirstBounds;
            _spec = view.Spec;
        }
        else
        {
            _ids = [.. _base.Shapes.Where(s => doc.Selection.Contains(s.Id) && !DeviceArrays.IsTagged(s)).Select(s => s.Id)];
            _firstBox = _base.Bounds(_ids);
            double px = Math.Round(_firstBox.Width + 5, 1), py = Math.Round(_firstBox.Height + 5, 1);
            _spec = new ArraySpec { Columns = 3, Rows = 2, PitchX = px, PitchY = py };
        }

        _cols.Value = _spec.Columns;
        _rows.Value = _spec.Rows;
        _pitchX.Value = (decimal)_spec.PitchX;
        _pitchY.Value = (decimal)_spec.PitchY;
        _x.Value = (decimal)Math.Round(_firstBox.Center.X, 3);
        _y.Value = (decimal)Math.Round(_firstBox.Center.Y, 3);
        _order.SelectedIndex = (int)_spec.Order;
        _keep.IsChecked = _spec.KeepNumbers;
        // The area defaults to what's left of the work area right and down from the first device.
        _areaW.Value = (decimal)Math.Max(1, Math.Round(machine.FieldW - _firstBox.MinX, 1));
        _areaH.Value = (decimal)Math.Max(1, Math.Round(machine.FieldH - _firstBox.MinY, 1));

        _countFields = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Columns", _cols, tip: "How many devices across."),
                Field("Rows", _rows, tip: "How many devices down."),
            },
        };
        _areaFields = new StackPanel
        {
            Spacing = 10,
            IsVisible = false,
            Children =
            {
                Field("Area width (mm)", _areaW, tip: "Width of the area to fill, from the first device's left edge."),
                Field("Area height (mm)", _areaH, tip: "Height of the area to fill, from the first device's top edge."),
            },
        };

        var allOn = Button("All on", tip: "Turn every device on");
        var allOff = Button("All off", tip: "Turn every device off (then click the ones to mark)");
        var invert = Button("Swap on and off", tip: "Turn the devices that are on off, and the ones that are off on");
        allOn.Click += (_, _) => SetOff([]);
        allOff.Click += (_, _) => SetOff([.. _spec.Cells()]);
        invert.Click += (_, _) => SetOff([.. _spec.Cells().Where(_spec.IsOn)]);
        _grid.Toggled += (cell, on) => { _spec = _spec.With(cell, on); Preview(); };

        var ok = new Button { Content = editing is null ? "Make the array" : "Done", IsDefault = true, Classes = { "primary" }, MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => Finish(editing is null ? "Device array" : "Change device array");
        cancel.Click += (_, _) => Close();
        Closed += (_, _) => { if (!_done) _doc.CancelEdit(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (editing is not null)
        {
            var release = new Button { Content = "Keep as plain copies", MinWidth = 90 };
            ToolTip.SetTip(release, "Stop it being an array: every device stays where it is as ordinary shapes, the ones that are off stay off");
            release.Click += (_, _) => { _doc.UpdateEdit(DeviceArrays.Release(_base, editing.Value)); Result = null; FinishAsIs("Keep device array as copies"); };
            var remove = new Button { Content = "Remove the array", MinWidth = 90 };
            ToolTip.SetTip(remove, "Take every copy away, leaving the first device as it was");
            remove.Click += (_, _) => { _doc.UpdateEdit(DeviceArrays.Remove(_base, editing.Value)); Result = null; FinishAsIs("Remove device array"); };
            buttons.Children.Add(release);
            buttons.Children.Add(remove);
            buttons.Children.Add(new Border { Width = 16 });
        }
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var left = new StackPanel
        {
            Spacing = 10,
            Width = 300,
            Children =
            {
                new TextBlock { Text = "MATRIX", Classes = { "h" } },
                Field("Size by", _sizeBy, tip: "Say how many columns and rows, or fill an area: as many as fit at this pitch."),
                _countFields,
                _areaFields,
                Field("Pitch X (mm)", _pitchX, tip: "Centre to centre across: from one device to the next."),
                Field("Pitch Y (mm)", _pitchY, tip: "Centre to centre down."),
                new TextBlock { Text = "FIRST DEVICE", Classes = { "h" }, Margin = new Thickness(0, 6, 0, 0) },
                Field("Centre X (mm)", _x, tip: "Where the middle of the first device (top left) is on the work area."),
                Field("Centre Y (mm)", _y),
                new TextBlock { Text = "NUMBERING", Classes = { "h" }, Margin = new Thickness(0, 6, 0, 0) },
                Field("Count", _order, tip: "The order the devices get their serial numbers or spreadsheet rows in."),
                _keep,
            },
        };
        ToolTip.SetTip(_keep, "On: a device that's off still uses up its number, so every place in the tray always gets the same one. Off: only the devices that are on are numbered, one after another.");

        var right = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "DEVICES", Classes = { "h" } },
                Hint("Click a device to turn it off or on; drag across several. The first device (outlined) is your design: change it, or any copy, on the canvas and they all follow."),
                new Border { Height = 300, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _grid },
                Row(allOn, allOff, invert),
                _info,
                _warn,
            },
        };
        var body = new Grid { ColumnDefinitions = new("Auto,24,*") };
        body.Children.Add(left);
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel { Spacing = 14, Children = { body, buttons } },
        };

        _sizeBy.SelectionChanged += (_, _) =>
        {
            bool area = _sizeBy.SelectedIndex == 1;
            _countFields.IsVisible = !area;
            _areaFields.IsVisible = area;
            Changed();
        };
        foreach (var n in new[] { _cols, _rows, _areaW, _areaH, _pitchX, _pitchY, _x, _y }) n.ValueChanged += (_, _) => Changed();
        _order.SelectionChanged += (_, _) => Changed();
        _keep.IsCheckedChanged += (_, _) => Changed();

        ok.IsEnabled = _ids.Count > 0;
        _doc.BeginEdit();
        _loading = false;
        Opened += (_, _) => Changed();
    }

    void SetOff(IEnumerable<Cell> off)
    {
        _spec = _spec with { Off = [.. off] };
        Preview();
    }

    /// <summary>How many fit along <paramref name="length"/> at <paramref name="pitch"/>, a device <paramref name="size"/> long.</summary>
    public static int Fit(double length, double size, double pitch) =>
        pitch <= 1e-9 ? 1 : Math.Clamp((int)Math.Floor((length - size) / pitch + 1e-9) + 1, 1, ArraySpec.MaxSide);

    void Changed()
    {
        if (_loading) return;
        double px = (double)(_pitchX.Value ?? 0), py = (double)(_pitchY.Value ?? 0);
        int cols = (int)(_cols.Value ?? 1), rows = (int)(_rows.Value ?? 1);
        if (_sizeBy.SelectedIndex == 1)
        {
            cols = Fit((double)(_areaW.Value ?? 0), _firstBox.Width, px);
            rows = Fit((double)(_areaH.Value ?? 0), _firstBox.Height, py);
        }
        _spec = (_spec with
        {
            Columns = cols,
            Rows = rows,
            PitchX = px,
            PitchY = py,
            Order = (CountOrder)Math.Max(0, _order.SelectedIndex),
            KeepNumbers = _keep.IsChecked == true,
        }).Normalised();
        if (_sizeBy.SelectedIndex == 1)
        {
            _loading = true;
            _cols.Value = _spec.Columns;
            _rows.Value = _spec.Rows;
            _loading = false;
        }
        Preview();
    }

    /// <summary>The design with the array as set now.</summary>
    DocState Build()
    {
        if (_ids.Count == 0) return _base;
        DocState s;
        Guid id;
        if (_editing is { } e) { s = DeviceArrays.SetSpec(_base, e, _spec); id = e; }
        else (s, id) = DeviceArrays.Create(_base, _ids, _spec);
        Result = id;
        var want = new Vec2((double)(_x.Value ?? 0), (double)(_y.Value ?? 0));
        var by = want - _firstBox.Center;
        return Math.Abs(by.X) > 1e-6 || Math.Abs(by.Y) > 1e-6 ? DeviceArrays.Move(s, id, by) : s;
    }

    void Preview()
    {
        _grid.Spec = _spec;
        var state = Build();
        _doc.UpdateEdit(state);
        var b = Result is { } id && DeviceArrays.Find(_doc.State, id) is { } v ? v.Bounds : Rect2.Empty;
        string size = b.IsEmpty ? "" : $", {b.Width:0.#} × {b.Height:0.#} mm overall";
        _info.Text = $"{_spec.OnCount} of {_spec.Count} devices on{size}." + (VariableText.Describe(_doc.State) is { } next ? $" Next run: {next}." : "");
        var warns = new List<string>();
        if (_spec.Columns * _spec.Rows >= ArraySpec.MaxCells) warns.Add($"At most {ArraySpec.MaxCells} devices in one array.");
        if (!b.IsEmpty && (Math.Abs(_spec.PitchX) < _firstBox.Width - 1e-6 && _spec.Columns > 1 || Math.Abs(_spec.PitchY) < _firstBox.Height - 1e-6 && _spec.Rows > 1))
            warns.Add("The pitch is smaller than the design, so devices overlap.");
        if (_spec.OnCount == 0) warns.Add("Every device is off: nothing will be marked.");
        _warn.Text = string.Join(" ", warns);
        _warn.IsVisible = warns.Count > 0;
    }

    void Finish(string label)
    {
        _done = true;
        _doc.EndEdit(label);
        if (Result is { } id && DeviceArrays.Find(_doc.State, id) is { } v) _doc.Select(v.Firsts.Select(f => f.Id));
        Close();
    }

    void FinishAsIs(string label)
    {
        _done = true;
        _doc.EndEdit(label);
        Close();
    }
}
