using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// What the user has: the machine, its attachments, and which specialist features to show.
/// Features that need something they don't have stay out of the ribbon. Changes save at once.
/// </summary>
public sealed class EquipmentPanel : UserControl
{
    static NumericUpDown Gear(AppSettings settings)
    {
        var n = UiKit.Number(1, 4, 1, "0");
        n.Value = Math.Clamp(settings.FanGear, 1, 4);
        n.ValueChanged += (_, _) => { if (n.Value is { } v) { settings.FanGear = (int)v; settings.Save(); } };
        return n;
    }

    static NumericUpDown Speed(AppSettings settings)
    {
        var n = UiKit.Number(10, 100, 10, "0");
        n.Value = Math.Clamp(settings.PurifierSpeed, 10, 100);
        n.ValueChanged += (_, _) => { if (n.Value is { } v) { settings.PurifierSpeed = (int)v; settings.Save(); } };
        return n;
    }

    /// <summary>What the chosen machine has, in a line.</summary>
    public static string Describe(PhotonCanvas.Core.MachineSetup m) =>
        m.Id == PhotonCanvas.Core.MachineSetup.F2Ultra.Id ? "40 W blue diode and 60 W MOPA fiber laser, 220 × 220 mm."
        : m.Kind == PhotonCanvas.Core.MachineKind.Grbl ? $"{m.Laser} {m.PowerW:0.#} W, {m.BedW:0} × {m.BedH:0} mm, over USB."
        : m.Profile.Uv is { } uv ? $"{uv.Watts:0} W UV laser (355 nm), {m.BedW:0} × {m.BedH:0} mm. Tools that need the blue diode or the MOPA are hidden. Not yet tried on a real F2 Ultra UV: start with a small test at low power."
        : $"{m.Laser}, {m.BedW:0} × {m.BedH:0} mm." + (m.Profile.HasMopa ? "" : " Tools that need the MOPA are hidden.") + " Not yet tried on this model.";

    /// <summary>Something was ticked or unticked, or the machine changed.</summary>
    public event Action? Changed;

    public EquipmentPanel(AppSettings settings)
    {
        CheckBox Box(string text, bool on, Action<bool> set, string tip)
        {
            var c = new CheckBox { Content = text, IsChecked = on };
            ToolTip.SetTip(c, tip);
            c.IsCheckedChanged += (_, _) => { set(c.IsChecked == true); settings.Save(); Changed?.Invoke(); };
            return c;
        }
        // The machine: the xTool models built in (GRBL machines are added and picked in Settings → Machines).
        // Choosing one with fewer lasers (the F2 Ultra UV) takes the tools that need the others out of the ribbon.
        var models = new[] { PhotonCanvas.Core.MachineSetup.F2Ultra }.Concat(PhotonCanvas.Core.MachineSetup.XToolFamily).ToList();
        var active = settings.ActiveSetup();
        var model = new ComboBox { ItemsSource = models.Select(m => m.Name).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(model, "Which xTool laser you have: it sets the work area, the lasers you can choose and the tools shown. Also in Settings → Machines.");
        var about = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var colour = Box("Colour on metal", settings.ShowColourOnMetal, v => settings.ShowColourOnMetal = v, "Colour tests, measuring colours with the close-up camera, and colour pictures on stainless or titanium (MOPA).");
        var gratings = Box("Diffraction gratings", settings.ShowDiffraction, v => settings.ShowDiffraction = v, "Rainbow shimmer pictures and the depth-map almost-hologram on polished stainless (MOPA).");
        void ShowModel(PhotonCanvas.Core.MachineSetup m)
        {
            about.Text = Describe(m);
            colour.IsVisible = gratings.IsVisible = m.Profile.HasMopa; // MOPA features: only with a MOPA
        }
        Control machineRow;
        if (active.Kind == PhotonCanvas.Core.MachineKind.Grbl)
        {
            machineRow = new TextBlock { Text = $"{active.Name} (GRBL). Change it in Settings → Machines.", TextWrapping = TextWrapping.Wrap };
            ShowModel(active);
        }
        else
        {
            model.SelectedIndex = Math.Max(0, models.FindIndex(m => m.Id == active.Id));
            ShowModel(models[model.SelectedIndex]);
            model.SelectionChanged += (_, _) =>
            {
                if (model.SelectedIndex < 0) return;
                var m = models[model.SelectedIndex];
                settings.ActiveMachine = m.Id == PhotonCanvas.Core.MachineSetup.F2Ultra.Id ? null : m.Id;
                settings.Save();
                ShowModel(m);
                Changed?.Invoke();
            };
            machineRow = model;
        }
        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "YOUR MACHINE", Classes = { "h" } },
                machineRow,
                about,
                new TextBlock { Text = "ATTACHMENTS", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                Box("RA2 Pro rotary (rollers and chuck)", settings.OwnsRa2, v => settings.OwnsRa2 = v, "For marking round things: cups, tumblers, rings."),
                Box("RA3 rotary (rollers and chuck)", settings.OwnsRa3, v => settings.OwnsRa3 = v, "The newer rotary."),
                Box("My own rotary (homemade or another make)", settings.OwnsCustomRotary, v => settings.OwnsCustomRotary = v, "A rotary on the machine's rotary port with its own scale: set it under Machine → Work mode, and correct it from a test turn."),
                Box("Conveyor feeder", settings.OwnsConveyor, v => settings.OwnsConveyor = v, "Feeds long pieces through, up to 610 mm."),
                Hint("Only the attachments you have show up under Machine → Work mode. With none, the Work mode button is hidden and jobs are always on the flat bed."),
                new TextBlock { Text = "FEATURES TO SHOW", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                colour,
                gratings,
                Box("G-code console", settings.ShowConsole, v => settings.ShowConsole = v, "Send commands straight to the machine, and keep macros (advanced)."),
                Hint("Hiding a feature only takes it out of the ribbon: nothing is lost, and ticking it again brings it back."),
                new TextBlock { Text = "EXTRACTION", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                Field("Exhaust fan gear", Gear(settings), tip: "Sent with each job to the exhaust fan (and an xTool duct fan): 1 lowest, 4 highest. xTool Studio sends 3."),
                Field("Purifier speed (%)", Speed(settings), tip: "Sent with each job to an xTool purifier. xTool Studio sends 50%."),
                Hint("Experimental: these are the commands xTool Studio puts at the start of every job, with the numbers changed. Check the fan sounds right on the first job."),
            },
        };
    }
}

/// <summary>Asked once (first start, or the first start after updating): what equipment there is.</summary>
public sealed class EquipmentWindow : Window
{
    public EquipmentWindow(AppSettings settings)
    {
        Title = "Your equipment";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var done = new Button { Content = "Done", Classes = { "primary" }, IsDefault = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "What do you have? Pick your machine and tick any attachments, so the features that need them appear. You can change this any time in Settings → Equipment.", TextWrapping = TextWrapping.Wrap },
                new EquipmentPanel(settings),
                done,
            },
        };
    }
}
