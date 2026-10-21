using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

    /// <summary>Something was ticked or unticked.</summary>
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
        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "YOUR MACHINE", Classes = { "h" } },
                new TextBlock { Text = "xTool F2 Ultra: 40 W blue diode and 60 W MOPA fiber laser, 220 × 220 mm.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "ATTACHMENTS", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                Box("RA2 Pro rotary (rollers and chuck)", settings.OwnsRa2, v => settings.OwnsRa2 = v, "For marking round things: cups, tumblers, rings."),
                Box("RA3 rotary (rollers and chuck)", settings.OwnsRa3, v => settings.OwnsRa3 = v, "The newer rotary."),
                Box("Conveyor feeder", settings.OwnsConveyor, v => settings.OwnsConveyor = v, "Feeds long pieces through, up to 610 mm."),
                Hint("Only the attachments you have show up under Laser → Work mode. With none, the Work mode button is hidden and jobs are always on the flat bed."),
                new TextBlock { Text = "FEATURES TO SHOW", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                Box("Colour on metal", settings.ShowColourOnMetal, v => settings.ShowColourOnMetal = v, "Colour tests, measuring colours with the close-up camera, and colour pictures on stainless or titanium (MOPA)."),
                Box("Diffraction gratings", settings.ShowDiffraction, v => settings.ShowDiffraction = v, "Rainbow shimmer pictures and the depth-map almost-hologram on polished stainless (MOPA)."),
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
                new TextBlock { Text = "What do you have? Tick any attachments, so the features that need them appear. You can change this any time in Settings → Equipment.", TextWrapping = TextWrapping.Wrap },
                new EquipmentPanel(settings),
                done,
            },
        };
    }
}
