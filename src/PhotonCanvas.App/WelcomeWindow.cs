using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// First run: what this is, the safety notice and disclaimer (must be
/// accepted), the connection keys, your equipment, calibrating the camera for the machine
/// picked, and how a job goes from drawing to laser.
/// Also opened later from the help menu (Getting started) (without the acceptance and equipment steps).
/// </summary>
public sealed class WelcomeWindow : Window
{
    /// <summary>
    /// What the camera step needs from the program: the machine in use, whether it is connected,
    /// connecting to it, opening camera calibration over this window, and whether that machine's
    /// camera is calibrated.
    /// </summary>
    public sealed record CameraSetup(Func<MachineSetup> Machine, Func<bool> Connected, Func<Task> Connect, Func<Window, Task> Calibrate, Func<bool> Calibrated);

    /// <summary>Bump when the notice changes so it is shown (and accepted) again.</summary>
    public const int TermsVersion = 1;

    public static string Product => AppIdentity.ProductName;

    public static readonly string SafetyText =
        "Lasers are dangerous. A laser cutter can blind you, start fires and release toxic fumes.\n\n" +
        "• Never look at the beam or its reflections. Keep the lid closed while it runs, or wear safety glasses rated for this laser's wavelengths (455 nm and 1064 nm on the F2 Ultra, 355 nm on the F2 Ultra UV).\n" +
        "• Never leave the machine running unattended. Keep a fire extinguisher nearby.\n" +
        "• Only cut materials you know are safe. PVC, vinyl and many other plastics release poisonous gas. Always ventilate.\n" +
        "• Check every job before pressing start: the preview is a guide, not a guarantee.";

    public static readonly string DisclaimerText =
        Product + " is free, independent software made by hobbyists. It is not made, endorsed or supported by xTool or Makeblock, " +
        "and using it may not be covered by your machine's warranty.\n\n" +
        "It is provided \"as is\", with no warranty of any kind. You use it entirely at your own risk: the author is not responsible for " +
        "injury, fire, damage to your machine, material or property, or anything else that happens as a result of using it. " +
        "The full terms are in the LICENSE file.";

    readonly bool _firstRun;
    readonly Func<Task<bool>> _setUpKeys;
    readonly Func<bool> _hasKeys;
    readonly ContentControl _page = new();
    readonly Button _back = new() { Content = "Back", MinWidth = 100 };
    readonly Button _next = new() { Content = "Next", Classes = { "primary" }, IsDefault = true, MinWidth = 120 };
    readonly TextBlock _steps = Hint();
    readonly CheckBox _accept = new() { Content = "I have read this, I understand the risks, and I accept these terms." };
    readonly TextBlock _keysState = new() { TextWrapping = TextWrapping.Wrap };
    readonly List<Func<Control>> _pages = [];
    readonly CameraSetup? _camera;
    int _index;

    /// <summary>True when the user accepted the safety notice and terms.</summary>
    public bool Accepted { get; private set; }

    public WelcomeWindow(bool firstRun, Func<Task<bool>> setUpKeys, Func<bool> hasKeys, AppSettings? equipment = null, CameraSetup? camera = null)
    {
        _firstRun = firstRun;
        _camera = camera;
        _setUpKeys = setUpKeys;
        _hasKeys = hasKeys;
        Title = firstRun ? $"Welcome to {Product}" : "Getting started";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _pages.Add(WelcomePage);
        if (firstRun) _pages.Add(SafetyPage);
        _pages.Add(KeysPage);
        if (equipment is not null)
            _pages.Add(() => new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Title2("Your equipment"),
                    Para("Pick your machine and tick any attachments you have, so the features that need them appear (and those it can't use stay hidden). You can change this any time in Settings → Equipment."),
                    new EquipmentPanel(equipment),
                },
            });
        if (camera is not null) _pages.Add(CameraPage);
        _pages.Add(HowPage);

        _accept.IsCheckedChanged += (_, _) => UpdateButtons();
        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) =>
        {
            if (_index < _pages.Count - 1) Go(_index + 1);
            else Close();
        };

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new DockPanel
            {
                Children =
                {
                    Dock(new DockPanel
                    {
                        Margin = new Thickness(0, 18, 0, 0),
                        Children =
                        {
                            Dock(_steps, Avalonia.Controls.Dock.Left),
                            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _back, _next } },
                        },
                    }, Avalonia.Controls.Dock.Bottom),
                    _page,
                },
            },
        };
        _steps.VerticalAlignment = VerticalAlignment.Center;
        Go(0);
    }

    static T Dock<T>(T c, Dock d) where T : Control
    {
        DockPanel.SetDock(c, d);
        return c;
    }

    void Go(int i)
    {
        _index = Math.Clamp(i, 0, _pages.Count - 1);
        _page.Content = _pages[_index]();
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool last = _index == _pages.Count - 1;
        bool onSafety = _firstRun && _index == 1;
        _back.IsVisible = _index > 0;
        _next.Content = last ? "Start designing" : onSafety ? "Accept and continue" : "Next";
        _next.IsEnabled = !onSafety || _accept.IsChecked == true;
        if (onSafety && _accept.IsChecked == true) Accepted = true;
        _steps.Text = $"Step {_index + 1} of {_pages.Count}";
    }

    static TextBlock Title2(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    static TextBlock Para(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };

    Control WelcomePage() => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            Title2(_firstRun ? $"Welcome to {Product}" : "Getting started"),
            Para($"{Product} designs laser jobs and sends them to your xTool F2 Ultra over your own network. " +
                 "It works offline: no account, no cloud, nothing about you or your designs is sent anywhere."),
            Para("This short guide covers safety, connecting to your machine, and how a job gets from the screen to the laser. It takes about a minute."),
        },
    };

    Control SafetyPage() => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            Title2("Safety and terms"),
            new Border
            {
                Classes = { "card" },
                Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "SAFETY", Classes = { "h" } }, Para(SafetyText) } },
            },
            new Border
            {
                Classes = { "card" },
                Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "NO WARRANTY · NOT AFFILIATED WITH XTOOL", Classes = { "h" } }, Para(DisclaimerText) } },
            },
            _accept,
        },
    };

    Control KeysPage()
    {
        var setUp = new Button { Content = "Set up connection keys…", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        void Refresh()
        {
            bool ok = _hasKeys();
            _keysState.Text = ok ? "✓ Connection keys are set up." : "Not set up yet. You can also do this later in Settings.";
            _keysState.Foreground = ok ? Brush("Ok") : Brush("TextMain");
            setUp.IsVisible = !ok;
        }
        setUp.Click += async (_, _) => { await _setUpKeys(); Refresh(); };
        Refresh();
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title2("Connect your laser"),
                Para("Turn the machine on and make sure it's on the same Wi-Fi or network as this computer (or plugged in by USB)."),
                Para($"{Product} needs three connection codes that xTool machines expect. They don't ship with this program; " +
                     "if xTool Studio is installed on this computer, the setup reads them from it in a few seconds."),
                setUp,
                _keysState,
                Para("Once that's done, PhotonCanvas finds and connects to the laser automatically. The first time, the machine may ask you to press its button to allow this computer."),
            },
        };
    }

    /// <summary>
    /// Camera calibration, explained for the machine picked on the equipment step: the F2 Ultra burns
    /// the marks with its blue diode over 220 × 220 mm, the F2 Ultra UV with its UV laser over
    /// 200 × 200 mm. Connect, lay a scrap, then the calibration window does the rest.
    /// </summary>
    Control CameraPage()
    {
        var cam = _camera!;
        var setup = cam.Machine();
        var connState = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var calState = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var connect = new Button { Content = "Connect", HorizontalAlignment = HorizontalAlignment.Left };
        var calibrate = new Button { Content = "Calibrate camera…", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        void Refresh()
        {
            bool on = cam.Connected(), done = cam.Calibrated();
            connState.Text = on ? "✓ Connected." : "Not connected yet.";
            connState.Foreground = on ? Brush("Ok") : Brush("TextMain");
            connect.IsVisible = !on;
            calibrate.Content = done ? "Calibrate again…" : "Calibrate camera…";
            calState.Text = done
                ? "✓ The camera is calibrated for this machine."
                : "Not calibrated yet. You can skip this and do it later: Machine → Calibrate camera.";
            calState.Foreground = done ? Brush("Ok") : Brush("TextMuted");
        }
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false;
            connState.Text = "Connecting…";
            try { await cam.Connect(); }
            finally { connect.IsEnabled = true; Refresh(); }
        };
        calibrate.Click += async (_, _) =>
        {
            calibrate.IsEnabled = false;
            try { await cam.Calibrate(this); }
            finally { calibrate.IsEnabled = true; Refresh(); }
        };
        Refresh();

        if (setup.Kind != MachineKind.XTool)
            return new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Title2("Calibrate the camera"),
                    Para($"{setup.Name} has no camera {Product} can use, so there is nothing to calibrate. Click Next."),
                },
            };

        var profile = setup.Profile;
        bool uv = profile.Uv is not null;
        string area = $"{profile.FieldW:0} × {profile.FieldH:0} mm";
        string forMachine = uv
            ? $"On the {setup.Name} the marks are burned with the UV laser over the whole {area} area. Plain card, plywood or anodized aluminium show UV marks well. " +
              "Do xTool's own lens and camera setup in xTool Studio first, and wear glasses rated for 355 nm if you watch with the lid open. " +
              "Not yet tried on a real F2 Ultra UV: if the marks come out too faint to find, raise the power a little and burn them again on fresh scrap."
            : profile.HasDiode
                ? $"On the {setup.Name} the marks are burned with the blue diode at low power over the whole {area} area. Plain card or thin plywood shows them clearly. " +
                  "One calibration does for both lasers; if the MOPA's marks land slightly off the diode's, Machine → Align lasers lines the two up."
                : $"On the {setup.Name} the marks are burned over the whole {area} area. Plain card or thin plywood shows them clearly." +
                  (setup.Untested ? " Not yet tried on this model." : "");

        static Control Step(string n, string text, params Control[] more)
        {
            var body = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } } };
            foreach (var c in more) body.Children.Add(c);
            return new Grid
            {
                ColumnDefinitions = new("30,*"),
                Children =
                {
                    new TextBlock { Text = n + ".", FontWeight = FontWeight.SemiBold },
                    Col1(body),
                },
            };
        }

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title2("Calibrate the camera"),
                Para("The camera's picture lets you place designs right on your material. Calibrating lines that picture up with where the laser actually marks. " +
                     "It takes about five minutes and one scrap sheet, and is kept for this machine."),
                new Border
                {
                    Classes = { "card" },
                    Child = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = ("For your " + setup.Name).ToUpperInvariant(), Classes = { "h" } }, Para(forMachine) } },
                },
                Step("1", "Turn the machine on and connect to it.", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { connect, connState } }),
                Step("2", $"Lay a flat scrap sheet over the whole {area} area."),
                Step("3", "Click Calibrate camera. Its window takes you through focusing, burning the marks (close the lid and press the machine's start button), taking a photo and saving.", calibrate),
                calState,
            },
        };
    }

    Control HowPage()
    {
        static Control Step(string n, string title, string body) => new Grid
        {
            ColumnDefinitions = new("36,*"),
            Children =
            {
                new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Brush("Accent"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = n, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                },
                Col1(new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
                    },
                }),
            },
        };
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Title2("How a job works"),
                Step("1", "Draw or import", "Use the Home tab of the ribbon, or drag in an SVG, DXF, PDF or picture (the Import tab brings in files too). The square is the machine's 220 × 220 mm work area."),
                Step("2", "Choose what the laser does", "Every shape belongs to a layer (left). A layer either cuts through, scores a line, engraves the area, or engraves a picture, at its own power and speed: click a layer to see and change its settings."),
                Step("3", "Find the right settings", "New material? Use Materials & Tests → Tests → Test grid to burn a grid of power and speed squares on a scrap, then copy the best one."),
                Step("4", "Send it", "In the job bar along the bottom, check the run order and time, then click Ship It. Nothing happens until you press the start button on the machine."),
                Hint("The ? at the top right → Getting started shows this again. Hover over any setting to see what it does."),
            },
        };
    }

    static Control Col1(Control c)
    {
        Grid.SetColumn(c, 1);
        return c;
    }
}
