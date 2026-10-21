using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>The work mode chosen in the settings, as the generator needs it.</summary>
public static class WorkModes
{
    public static readonly string[] Attachments = ["RA2 Pro · rollers", "RA2 Pro · chuck", "RA3 · rollers", "RA3 · chuck"];

    /// <summary>Longest unrolled length the rotary takes (xTool Studio's limit).</summary>
    public const double MaxCircumferenceMm = 375;

    /// <summary>Is this attachment (index into <see cref="Attachments"/>) one the user has?</summary>
    public static bool Owned(AppSettings s, int attachment) => attachment >= 2 ? s.OwnsRa3 : s.OwnsRa2;

    /// <summary>The work mode in the settings is one the user's equipment can do (else the flat bed is used).</summary>
    public static bool Usable(AppSettings s) => s.ActiveSetup().Kind == MachineKind.XTool && s.WorkMode switch
    {
        "Rotary" => Owned(s, s.RotaryAttachment),
        "Conveyor" => s.OwnsConveyor,
        _ => true,
    };

    public static WorkMode From(AppSettings s)
    {
        if (!Usable(s)) return new WorkMode.Flat();
        switch (s.WorkMode)
        {
            case "Rotary":
                double circ = Math.PI * Math.Max(1, s.RotaryDiameterMm);
                bool chuck = s.RotaryAttachment is 1 or 3, ra3 = s.RotaryAttachment >= 2;
                // Rollers: a fixed scale (four times as much on the RA3); chuck: 128 steps per turn of the object.
                return new WorkMode.Rotary(chuck ? 128 / circ : 0.59917 * (ra3 ? 4 : 1), chuck ? 1 : -1, Math.Min(circ, MaxCircumferenceMm));
            case "Conveyor":
                return new WorkMode.Conveyor(s.ConveyorUMultiple, s.ConveyorAngle);
            default:
                return new WorkMode.Flat();
        }
    }

    /// <summary>The work area the canvas shows for the mode: the bed, the object unrolled, or the belt's length.</summary>
    public static MachineProfile Area(AppSettings s) => s.ActiveSetup() is { } m && m.Id != MachineSetup.F2Ultra.Id ? m.Profile : From(s) switch
    {
        WorkMode.Rotary r => MachineProfile.F2Ultra with { FieldH = Math.Round(r.CircumferenceMm, 2) },
        WorkMode.Conveyor c => MachineProfile.F2Ultra with { FieldW = c.LengthMm },
        _ => MachineProfile.F2Ultra,
    };

    public static string Describe(AppSettings s) => !Usable(s) ? "Flat bed" : From(s) switch
    {
        WorkMode.Rotary r => $"Rotary ({Attachments[Math.Clamp(s.RotaryAttachment, 0, 3)]}), Ø {s.RotaryDiameterMm:0.#} mm: {MachineProfile.F2Ultra.FieldW:0} × {r.CircumferenceMm:0.#} mm unrolled",
        WorkMode.Conveyor c => $"Conveyor: {c.LengthMm:0} × {MachineProfile.F2Ultra.FieldH:0} mm",
        _ => "Flat bed",
    };
}

/// <summary>Flat bed, rotary or conveyor, and the attachment's settings.</summary>
public sealed class WorkModeWindow : Window
{
    readonly AppSettings _settings;
    readonly MachinePanel _machine;
    readonly Segmented _mode;
    readonly List<string> _modes = ["Flat"];
    readonly List<int> _attachments;
    readonly ComboBox _attach = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _diameter = Number(2, 200, 1, "0.##"), _rate = Number(90, 105, 0.1m, "0.##"), _angle = Number(-10, 10, 0.01m, "0.###");
    readonly Control _rotary, _conveyor;
    readonly TextBlock _info = Hint();

    public WorkModeWindow(AppSettings settings, MachinePanel machine)
    {
        _settings = settings;
        _machine = machine;
        // Only what the user has (Settings → Equipment).
        if (settings.OwnsRa2 || settings.OwnsRa3) _modes.Add("Rotary");
        if (settings.OwnsConveyor) _modes.Add("Conveyor");
        _mode = new([.. _modes.Select(m => m switch { "Rotary" => ("Rotary", (string?)"IcRotary"), "Conveyor" => ("Conveyor", "IcConveyor"), _ => ("Flat bed", (string?)null) })]);
        _attachments = [.. Enumerable.Range(0, WorkModes.Attachments.Length).Where(i => WorkModes.Owned(settings, i))];
        _attach.ItemsSource = _attachments.Select(i => WorkModes.Attachments[i]).ToList();
        Title = "Work mode";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _mode.SelectedIndex = Math.Max(0, _modes.IndexOf(WorkModes.Usable(settings) ? settings.WorkMode : "Flat"));
        _attach.SelectedIndex = Math.Max(0, _attachments.IndexOf(settings.RotaryAttachment));
        _diameter.Value = (decimal)settings.RotaryDiameterMm;
        _rate.Value = (decimal)Math.Round(100 * 0.74592 / Math.Max(0.01, settings.ConveyorUMultiple), 2);
        _angle.Value = (decimal)settings.ConveyorAngle;
        ToolTip.SetTip(_diameter, "The object's diameter where it's marked. With rollers it sets how far round the design goes; with the chuck it also sets the scale.");
        ToolTip.SetTip(_rate, "The belt's calibration from xTool Studio (Conveyor calibration, 90–105 %, normally 100).");
        ToolTip.SetTip(_angle, "The belt's angle compensation from xTool Studio's conveyor calibration (0 if never calibrated).");
        var read = Button("Read from the machine", tip: "Use the calibration stored on the machine (needs a connection).");
        read.Click += async (_, _) => await ReadCalibration();
        _rotary = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Attachment", _attach),
                Field("Object Ø (mm)", _diameter),
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
                Text = "Attachment work modes are experimental: the commands follow xTool Studio's own (mechanical axis U), but haven't been run on your machine yet. " +
                       "Fit the attachment as xTool describes, and try a small mark on scrap first.",
            },
        };
        var ok = new Button { Content = "OK", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { Save(); Close(true); };
        cancel.Click += (_, _) => Close(false);
        _mode.Changed += _ => Update();
        _attach.SelectionChanged += (_, _) => Update();
        _diameter.ValueChanged += (_, _) => Update();
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _mode, _rotary, _conveyor, warn, _info,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                },
            },
        };
        Update();
    }

    void Update()
    {
        string mode = _modes[Math.Max(0, _mode.SelectedIndex)];
        _rotary.IsVisible = mode == "Rotary";
        _conveyor.IsVisible = mode == "Conveyor";
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
        _settings.ConveyorUMultiple = 100 * 0.74592 / Math.Max(1, (double)(_rate.Value ?? 100));
        _settings.ConveyorAngle = (double)(_angle.Value ?? 0);
        _settings.Save();
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
