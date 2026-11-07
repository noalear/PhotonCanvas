using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Flat bed, rotary or conveyor, and the attachment's settings.</summary>
public sealed class WorkModeWindow : Window
{
    readonly AppSettings _settings;
    readonly MachinePanel _machine;
    readonly Segmented _mode;
    readonly List<string> _modes = ["Flat"];
    readonly List<int> _attachments;
    readonly ComboBox _attach = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _diameter = Number(2, 200, 1, "0.##"), _rate = Number(90, 105, 0.1m, "0.##"), _angle = Number(-10, 10, 0.01m, "0.###"), _line = Number(1, 219, 1, "0.#");
    readonly Control _rotary, _conveyor, _custom;
    // The user's own rotary: chuck or rollers, its scale, which way it turns, and correcting the scale from a test.
    readonly Segmented _customKind = new([("Chuck", (string?)null), ("Rollers", null)]);
    readonly NumericUpDown _turnUnits = Number(1, 100000, 1, "0.####"), _rollerScale = Number(0.001m, 1000, 0.01m, "0.#####"), _asked = Number(0.1m, 100, 1, "0.##"), _seen = Number(0.01m, 100, 0.05m, "0.##");
    readonly CheckBox _reverse = new() { Content = "It turns the other way" };
    readonly TextBlock _calNote = Hint();
    // Turning the rotary by hand from here (a laser-off job), and turning back what a stopped job left.
    readonly NumericUpDown _turnDeg = Number(-3600, 3600, 15, "0.#"), _nowMade = Number(0.05m, 20, 0.05m, "0.###"), _perTurn = Number(1, 100000, 0.001m, "0.####");
    // The units a turn shown when the window opened (or last saved): only a change is kept.
    double _perTurnShown;
    readonly TextBlock _nowNote = Hint();
    readonly TextBlock _leftNote = Hint();
    readonly Button _returnBtn = Button("Return rotary to start", tip: "Turn the object back to where the stopped job started (laser off).");
    readonly TextBlock _info = Hint();

    public WorkModeWindow(AppSettings settings, MachinePanel machine)
    {
        _settings = settings;
        _machine = machine;
        // Only what the user has (Settings → Equipment).
        if (WorkModes.OwnsRotary(settings)) _modes.Add("Rotary");
        if (settings.OwnsConveyor) _modes.Add("Conveyor");
        _mode = new([.. _modes.Select(m => m switch { "Rotary" => ("Rotary", (string?)"IcRotary"), "Conveyor" => ("Conveyor", "IcConveyor"), _ => ("Flat bed", (string?)null) })]);
        _attachments = [.. Enumerable.Range(0, WorkModes.Attachments.Length).Where(i => WorkModes.Owned(settings, i))];
        _attach.ItemsSource = _attachments.Select(i => WorkModes.Attachments[i]).ToList();
        Title = "Work mode";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _mode.SelectedIndex = Math.Max(0, _modes.IndexOf(WorkModes.Usable(settings) ? settings.WorkMode : "Flat"));
        _attach.SelectedIndex = Math.Max(0, _attachments.IndexOf(settings.RotaryAttachment));
        _diameter.Value = (decimal)settings.RotaryDiameterMm;
        _line.Value = (decimal)Math.Clamp(settings.RotaryLineMm, 1, 219);
        _rate.Value = (decimal)Math.Round(100 * 0.74592 / Math.Max(0.01, settings.ConveyorUMultiple), 2);
        _angle.Value = (decimal)settings.ConveyorAngle;
        _customKind.SelectedIndex = settings.CustomRotaryChuck ? 0 : 1;
        _turnUnits.Value = (decimal)settings.CustomRotaryTurnUnits;
        _rollerScale.Value = (decimal)settings.CustomRotaryRollerScale;
        _reverse.IsChecked = settings.CustomRotaryReverse;
        _asked.Value = 1; _seen.Value = 1;
        ToolTip.SetTip(_turnUnits, "The machine's units for one whole turn of the object in the chuck. xTool's chucks take 128; a homemade one with other gearing or microstepping needs its own number. Correct it below from a test turn.");
        ToolTip.SetTip(_rollerScale, "The machine's scale for rollers (mm of surface): 0.59917 on the RA2 Pro. Correct it below from a test.");
        ToolTip.SetTip(_reverse, "Tick if a test mark comes out mirrored round the object (the motor is wired the other way round from xTool's).");
        ToolTip.SetTip(_asked, "How many turns the test job asked for: a design as tall as the work area is one turn.");
        ToolTip.SetTip(_seen, "How many turns the object really made (put a tape flag on it and count; a quarter is 0.25).");
        var correct = Button("Correct the scale", tip: "Change the scale so the asked-for turns come out right next time.");
        correct.Click += (_, _) => Correct();
        ToolTip.SetTip(_line, "Where the laser draws across the object, in mm from the back edge of the 220 mm work field (110 is the middle). Set it over the top of the object, along the rotary's axis: frame a job to see the line, and move it until it sits there.");
        ToolTip.SetTip(_diameter, "The object's diameter where it's marked. With rollers it sets how far round the design goes; with the chuck it also sets the scale.");
        ToolTip.SetTip(_rate, "The belt's calibration from xTool Studio (Conveyor calibration, 90–105 %, normally 100).");
        ToolTip.SetTip(_angle, "The belt's angle compensation from xTool Studio's conveyor calibration (0 if never calibrated).");
        var read = Button("Read from the machine", tip: "Use the calibration stored on the machine (needs a connection).");
        read.Click += async (_, _) => await ReadCalibration();
        correct.HorizontalAlignment = HorizontalAlignment.Stretch;
        _calNote.IsVisible = false; // until a correction has something to say
        _turnDeg.Value = 90;
        ToolTip.SetTip(_turnDeg, "Degrees round the object. Positive turns it the way a design runs down the page; negative the other way.");
        var turn = Button("Turn", tip: "Turn the rotary by this much now, with the laser off (it saves the settings above first).");
        turn.Click += async (_, _) => await TurnBy();
        _returnBtn.Click += async (_, _) => { Save(); await RotaryTurn.ReturnAsync(this, _settings, _machine); UpdateLeft(); };
        var turnRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        turnRow.Children.Add(Field("Turn by (°)", _turnDeg));
        Grid.SetColumn(turn, 1);
        turn.VerticalAlignment = VerticalAlignment.Bottom;
        turn.MinWidth = 90;
        turnRow.Children.Add(turn);
        _nowMade.Value = 1;
        ToolTip.SetTip(_nowMade, "How many turns the object really made when you clicked Turn once (a quarter past is 1.25, a quarter short 0.75).");
        var turnOnce = Button("Turn once", tip: "Turn the object one whole turn, to check the turning scale against a tape mark.");
        turnOnce.Click += async (_, _) => await TurnOnce();
        var letGo = Button("Let the motor go", tip: "After a turn the motor holds the object still. This lets it go limp so you can turn it by hand.");
        letGo.Click += async (_, _) =>
        {
            try { await _machine.FreeRotaryAsync(); }
            catch (PhotonCanvas.Device.DeviceException e) { await Dialogs.Message(this, "Couldn't let it go", e.Message); }
        };
        var stopTurn = Button("Stop turning", tip: "Stop a turn part way.");
        stopTurn.Click += async (_, _) =>
        {
            try { await _machine.StopRotaryAsync(); }
            catch (PhotonCanvas.Device.DeviceException e) { await Dialogs.Message(this, "Couldn't stop it", e.Message); }
        };
        var fixNow = Button("Correct", tip: "Change the turning scale so one turn comes out right next time.");
        fixNow.Click += (_, _) =>
        {
            Save();
            double was = RotaryTurn.KnownNowScale(_settings), made = (double)(_nowMade.Value ?? 1);
            _settings.RotaryNowScale = was * 1 / made;
            _settings.Save();
            ShowPerTurn();
            _nowNote.IsVisible = true;
            _nowNote.Text = Math.Abs(made - 1) < 1e-9 ? "Nothing to correct: it made one turn." : $"Corrected ({was:0.####} → {_settings.RotaryNowScale:0.####}). Turn once again to check.";
            _nowMade.Value = 1;
        };
        _nowNote.IsVisible = false;
        var madeRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        madeRow.Children.Add(Field("It made (turns)", _nowMade));
        Grid.SetColumn(fixNow, 1);
        fixNow.VerticalAlignment = VerticalAlignment.Bottom;
        fixNow.MinWidth = 90;
        madeRow.Children.Add(fixNow);
        ToolTip.SetTip(_perTurn, "The machine's units for one whole turn when it turns the rotary outside a job (straight away). It's worked out from your scale and the machine's belt factor; type the exact figure if you've measured it over many turns.");
        var turnPanel = new Border
        {
            Background = Brush("Bg2"), CornerRadius = new(6), Padding = new(12, 10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "TURN THE ROTARY", Classes = { "h" } },
                    Hint("These turn the object straight away, with the laser off. Keep hands clear of the chuck. After a turn the motor holds it still."),
                    _leftNote, _returnBtn, turnRow, stopTurn,
                    Hint("Check the turning once: put a tape mark on the chuck, click Turn once, and say how many turns it really made."),
                    Row(turnOnce, letGo), madeRow, _nowNote,
                    Field("Turn scale", _perTurn),
                },
            },
        };
        _custom = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Holds the object", _customKind),
                Field("Units a turn", _turnUnits),
                Field("Roller scale", _rollerScale),
                _reverse,
                new Border
                {
                    Background = Brush("Bg2"), CornerRadius = new(6), Padding = new(12, 10),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "CHECK THE SCALE", Classes = { "h" } },
                            Hint("Run a design as tall as the work area on scrap: it should turn the object exactly once. Count only the slow turn while it draws (the quick turns before and after just go to the start and back)."),
                            Field("Turns asked", _asked),
                            Field("Turns made", _seen),
                            correct, _calNote,
                        },
                    },
                },
            },
        };
        _rotary = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Attachment", _attach),
                Field("Object Ø (mm)", _diameter),
                Field("Laser line (mm)", _line),
                _custom,
                turnPanel,
                Hint("The work area becomes the field's width × once round the object. Design Y goes round it; the middle of the design faces the laser at the start."),
            },
        };
        _conveyor = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row(Field("Belt calibration %", _rate), Field("Angle", _angle)),
                read,
                Hint("The work area becomes 610 × 220 mm: design X moves the belt, the laser draws across it. Engraving lines always run across the belt."),
            },
        };
        var warn = new Border
        {
            Background = Brush("Bg2"), CornerRadius = new(6), Padding = new(10, 6),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Attachment work modes are new: the commands follow xTool Studio's own (mechanical axis U). " +
                       "Try a small mark on scrap first.",
            },
        };
        var ok = new Button { Content = "OK", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { Save(); Close(true); };
        cancel.Click += (_, _) => Close(false);
        _mode.Changed += _ => Update();
        _attach.SelectionChanged += (_, _) => Update();
        _diameter.ValueChanged += (_, _) => Update();
        _customKind.Changed += _ => Update();
        // The rotary's settings are tall: they scroll above the buttons on a short screen.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(18, 10, 18, 18), Children = { cancel, ok } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel
        {
            Children =
            {
                buttons,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Margin = new(18, 18, 18, 0),
                        Children = { _mode, _rotary, _conveyor, warn, _info },
                    },
                },
            },
        };
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } sc) MaxHeight = Math.Max(400, sc.WorkingArea.Height / sc.Scaling - 60);
        };
        Update();
        UpdateLeft();
        ShowPerTurn();
    }

    /// <summary>The motor units of one turn with the saved settings (null off the rotary).</summary>
    double? UnitsPerTurn() => WorkModes.From(_settings) is WorkMode.Rotary m
        ? RotaryReturn.UnitsPerTurn(m, Math.PI * Math.Max(1, _settings.RotaryDiameterMm)) : null;

    /// <summary>Show the units a turn outside jobs, from the scale turns use now.</summary>
    void ShowPerTurn()
    {
        if (UnitsPerTurn() is not { } upt) return;
        _perTurnShown = Math.Round(upt * RotaryTurn.KnownNowScale(_settings), 4);
        _perTurn.Value = Math.Clamp((decimal)_perTurnShown, _perTurn.Minimum, _perTurn.Maximum);
    }

    /// <summary>What a stopped job left turned, and the button that turns it back.</summary>
    void UpdateLeft()
    {
        bool left = _settings.RotaryLeftU is not null;
        _leftNote.IsVisible = _returnBtn.IsVisible = left;
        _leftNote.Text = left ? $"A stopped job left the object turned about {RotaryTurn.Describe(_settings)} from where it started (worked out from how far it had got, so check your mark)." : "";
    }

    /// <summary>Turn the rotary by the degrees asked, with the settings in this window.</summary>
    async Task TurnBy()
    {
        Save();
        if (WorkModes.From(_settings) is not WorkMode.Rotary mode)
        {
            await Dialogs.Message(this, "Not on the rotary", "Choose Rotary above (and a machine that has one) first.");
            return;
        }
        double deg = (double)(_turnDeg.Value ?? 0);
        if (Math.Abs(deg) < 0.1) return;
        double circ = Math.PI * Math.Max(1, _settings.RotaryDiameterMm);
        double u = RotaryReturn.UFor(deg, mode, circ);
        bool sent = await RotaryTurn.TurnAsync(this, _settings, _machine, RotaryReturn.UnitsFor(deg, mode, circ), mode, u,
            $"Turn the object {Math.Abs(deg):0.#}° {(deg > 0 ? "forward (the way a design runs down the page)" : "back (against the way a design runs down the page)")}.");
        // Turning by hand from here moves the object relative to where a stopped job started too.
        if (sent && _settings.RotaryLeftU is not null && _settings.RotaryLeftScale > 0)
        {
            RotaryReturn.NoteLeft(_settings, u, mode);
            _settings.Save();
            UpdateLeft();
        }
    }

    /// <summary>One whole turn, for checking the turning scale against a tape mark.</summary>
    async Task TurnOnce()
    {
        Save();
        if (WorkModes.From(_settings) is not WorkMode.Rotary mode)
        {
            await Dialogs.Message(this, "Not on the rotary", "Choose Rotary above (and a machine that has one) first.");
            return;
        }
        double circ = Math.PI * Math.Max(1, _settings.RotaryDiameterMm);
        await RotaryTurn.TurnAsync(this, _settings, _machine, RotaryReturn.UnitsFor(360, mode, circ), mode, RotaryReturn.UFor(360, mode, circ), "Turn the object once.");
    }

    void Update()
    {
        string mode = _modes[Math.Max(0, _mode.SelectedIndex)];
        _rotary.IsVisible = mode == "Rotary";
        _conveyor.IsVisible = mode == "Conveyor";
        bool custom = _attach.SelectedIndex >= 0 && _attach.SelectedIndex < _attachments.Count && _attachments[_attach.SelectedIndex] == WorkModes.CustomRotary;
        _custom.IsVisible = custom;
        if (_turnUnits.Parent is Control tu) tu.IsVisible = _customKind.SelectedIndex == 0;
        if (_rollerScale.Parent is Control rs) rs.IsVisible = _customKind.SelectedIndex == 1;
        double circ = Math.PI * (double)(_diameter.Value ?? 60);
        _info.Text = mode switch
        {
            "Rotary" => $"Once round: {circ:0.#} mm" + (circ > WorkModes.MaxCircumferenceMm ? $" (only the first {WorkModes.MaxCircumferenceMm:0} mm can be used)." : "."),
            "Conveyor" => "",
            _ => "The usual 220 × 220 mm work area.",
        };
    }

    void Save()
    {
        _settings.WorkMode = _modes[Math.Max(0, _mode.SelectedIndex)];
        if (_attach.SelectedIndex >= 0 && _attach.SelectedIndex < _attachments.Count) _settings.RotaryAttachment = _attachments[_attach.SelectedIndex];
        _settings.RotaryDiameterMm = (double)(_diameter.Value ?? 60);
        _settings.RotaryLineMm = (double)(_line.Value ?? 110);
        _settings.CustomRotaryChuck = _customKind.SelectedIndex != 1;
        _settings.CustomRotaryTurnUnits = (double)(_turnUnits.Value ?? 128);
        _settings.CustomRotaryRollerScale = (double)(_rollerScale.Value ?? 0.59917m);
        _settings.CustomRotaryReverse = _reverse.IsChecked == true;
        _settings.ConveyorUMultiple = 100 * 0.74592 / Math.Max(1, (double)(_rate.Value ?? 100));
        _settings.ConveyorAngle = (double)(_angle.Value ?? 0);
        // A typed units a turn outside jobs becomes the turning scale for this rotary.
        if (_perTurn.Value is { } pt && Math.Abs((double)pt - _perTurnShown) > 1e-6 && UnitsPerTurn() is { } upt && upt > 0)
        {
            _settings.RotaryNowScale = (double)pt / upt;
            _perTurnShown = (double)pt;
        }
        _settings.Save();
    }

    /// <summary>Scale the units a turn (or the roller scale) by turns asked ÷ turns made.</summary>
    void Correct()
    {
        double asked = (double)(_asked.Value ?? 1), seen = (double)(_seen.Value ?? 1);
        var field = _customKind.SelectedIndex == 0 ? _turnUnits : _rollerScale;
        double was = (double)(field.Value ?? 0), now = WorkModes.Calibrated(was, asked, seen);
        field.Value = Math.Clamp((decimal)Math.Round(now, 5), field.Minimum, field.Maximum);
        _calNote.IsVisible = true;
        _calNote.Text = Math.Abs(now - was) < 1e-9 ? "Nothing to correct: it turned as asked." : $"Changed from {was:0.#####} to {now:0.#####}. Press OK to keep it, then run the test again to check.";
        _asked.Value = 1; _seen.Value = 1;
    }

    async Task ReadCalibration()
    {
        var configs = await _machine.GetConfigsAsync();
        double? rate = Num(configs?["ConveyorURate"]), angle = Num(configs?["ConveyorAngleCompensate"]);
        if (rate is { } r && r > 0) _rate.Value = (decimal)Math.Round(100 * 0.74592 / r, 2);
        if (angle is { } a) _angle.Value = (decimal)a;
        _info.Text = rate is null && angle is null ? "The machine didn't report a conveyor calibration (not connected, or never calibrated)." : "Read the machine's conveyor calibration.";

        static double? Num(System.Text.Json.Nodes.JsonNode? n)
        {
            if (n is null) return null;
            try { return n.GetValue<double>(); }
            catch (Exception e) when (e is FormatException or InvalidOperationException)
            {
                return double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
            }
        }
    }
}

/// <summary>
/// Layer by layer (screen printing, several colours or materials): pick a layer, ship it, change
/// the screen or material, pick the next. The job bar marks only the picked layer.
/// </summary>
public sealed class LayerByLayerWindow : Window
{
    readonly JobBar _job;
    readonly StackPanel _list = new() { Spacing = 4 };
    readonly List<Layer> _layers;

    public LayerByLayerWindow(Document doc, JobBar job)
    {
        _job = job;
        _layers = [.. JobBuilder.OutputLayers(doc.State).Where(l => l.ParentId is null && doc.State.ShapesFor(l).Any())];
        Title = "Layer by layer";
        Width = 420;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var top = Hint("Pick a layer and Ship It: only that layer (and its sub-layers) is marked. Then change the screen, colour or material and pick the next. For screen printing, flip the design (Arrange → Flip across) first.");
        DockPanel.SetDock(top, Dock.Top);
        var close = new Button { Content = "Done", IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, close, new ScrollViewer { Content = _list, Margin = new(0, 10, 0, 0) } } };
        Closed += (_, _) => _job.OnlyLayer = null;
        for (int i = 0; i < _layers.Count; i++)
        {
            var l = _layers[i];
            var b = new Button { Content = $"{i + 1}. {l.Name}", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) => Pick(l);
            _list.Children.Add(b);
        }
        if (_layers.Count > 0) Pick(_layers[0]);
    }

    void Pick(Layer l)
    {
        _job.OnlyLayer = l.Id;
        for (int i = 0; i < _list.Children.Count; i++)
            if (_list.Children[i] is Button b) b.Classes.Set("primary", _layers[i].Id == l.Id);
    }
}

/// <summary>
/// Mark a design bigger than the work area in sections: pick one, mark it, move the material by
/// the amount shown, pick the next. The job bar marks whichever section is picked.
/// </summary>
public sealed class SectionsWindow : Window
{
    readonly Document _doc;
    readonly JobBar _job;
    readonly NumericUpDown _w = Number(10, 220, 5, "0.#"), _h = Number(10, 220, 5, "0.#");

    static Control Above(string label, NumericUpDown n)
    {
        n.ShowButtonSpinner = false;
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 }, n } };
    }
    readonly StackPanel _list = new() { Spacing = 4 };
    readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap };
    List<Section> _sections = [];

    public SectionsWindow(Document doc, JobBar job)
    {
        _doc = doc;
        _job = job;
        Title = "Mark in sections";
        Width = 470;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _w.Value = 200;
        _h.Value = 200;
        _w.ValueChanged += (_, _) => Plan();
        _h.ValueChanged += (_, _) => Plan();
        var top = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _info,
                Row(Above("Section width (mm)", _w), Above("Section height (mm)", _h)),
                Hint("Set the material against a fence or stop. After each section, slide it by the amount shown (from where it was for section 1), " +
                     "then pick the next section here and Ship It. The camera picture shows whether it lines up."),
            },
        };
        DockPanel.SetDock(top, Dock.Top);
        var close = new Button { Content = "Done", IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, close, new ScrollViewer { Content = _list, Margin = new(0, 10, 0, 0) } } };
        Closed += (_, _) => _job.Section = null;
        Plan();
    }

    void Plan()
    {
        var all = _doc.State.Bounds(JobBuilder.OutputLayers(_doc.State).SelectMany(l => _doc.State.ShapesOn(l.Id)).Select(s => s.Id));
        _sections = Section.Plan(all, (double)(_w.Value ?? 200), (double)(_h.Value ?? 200), MachineProfile.F2Ultra);
        _info.Text = all.IsEmpty ? "Nothing to mark." : $"The design is {all.Width:0.#} × {all.Height:0.#} mm: {_sections.Count} section{(_sections.Count == 1 ? "" : "s")}.";
        _list.Children.Clear();
        foreach (var s in _sections)
        {
            string move = s.MaterialMove == default ? "as it is" : MoveText(s.MaterialMove);
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = $"Section {s.Index + 1} · row {s.Row + 1}, column {s.Col + 1}", FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = $"Material: {move}", Classes = { "muted" }, FontSize = 12 },
                    },
                },
            };
            b.Click += (_, _) => Pick(s);
            _list.Children.Add(b);
        }
        if (_sections.Count > 0) Pick(_sections[0]);
    }

    static string MoveText(Vec2 m)
    {
        var parts = new List<string>();
        if (Math.Abs(m.X) > 1e-6) parts.Add($"{Math.Abs(m.X):0.##} mm {(m.X < 0 ? "left" : "right")}");
        if (Math.Abs(m.Y) > 1e-6) parts.Add($"{Math.Abs(m.Y):0.##} mm {(m.Y < 0 ? "towards the back" : "towards the front")}");
        return "move it " + string.Join(" and ", parts) + " from where it was for section 1";
    }

    void Pick(Section s)
    {
        _job.Section = s;
        for (int i = 0; i < _list.Children.Count; i++)
            if (_list.Children[i] is Button b) b.Classes.Set("primary", i == s.Index);
    }
}
