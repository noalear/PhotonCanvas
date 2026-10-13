using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// First run: what this is, the safety notice and disclaimer (must be
/// accepted), the connection keys, and how a job goes from drawing to laser.
/// Also opened later from Help → Getting started (without the acceptance step).
/// </summary>
public sealed class WelcomeWindow : Window
{
    /// <summary>Bump when the notice changes so it is shown (and accepted) again.</summary>
    public const int TermsVersion = 1;

    public static string Product => AppIdentity.ProductName;

    public static readonly string SafetyText =
        "Lasers are dangerous. A laser cutter can blind you, start fires and release toxic fumes.\n\n" +
        "• Never look at the beam or its reflections. Keep the lid closed while it runs, or wear safety glasses rated for this laser's wavelengths (455 nm and 1064 nm).\n" +
        "• Never leave the machine running unattended. Keep a fire extinguisher nearby.\n" +
        "• Only cut materials you know are safe. PVC, vinyl and many other plastics release poisonous gas. Always ventilate.\n" +
        "• Check every job before pressing start: the preview is a guide, not a guarantee.";

    public static readonly string DisclaimerText =
        Product + " is free, independent software made by hobbyists. It is not made, endorsed or supported by xTool or Makeblock, " +
        "and using it may not be covered by your machine's warranty.\n\n" +
        "It is provided \"as is\", with no warranty of any kind. You use it entirely at your own risk: the authors are not responsible for " +
        "injury, fire, damage to your machine, material or property, or anything else that happens as a result of using it. " +
        "The full terms are in the LICENSE file (GNU General Public License, version 3, sections 15 and 16).";

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
    int _index;

    /// <summary>True when the user accepted the safety notice and terms.</summary>
    public bool Accepted { get; private set; }

    public WelcomeWindow(bool firstRun, Func<Task<bool>> setUpKeys, Func<bool> hasKeys)
    {
        _firstRun = firstRun;
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
            _keysState.Text = ok ? "✓ Connection keys are set up." : "Not set up yet. You can also do this later on the Machine tab.";
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
                Para("Once that's done, the Machine tab finds and connects to the laser automatically. The first time, the machine may ask you to press its button to allow this computer."),
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
                Step("1", "Draw or import", "Use the tools on the left, or drag in an SVG, DXF, PDF or picture (File → Import). The square is the machine's 220 × 220 mm work area."),
                Step("2", "Choose what the laser does", "Every shape belongs to a layer (left panel). A layer either cuts through, scores a line, engraves the area, or engraves a picture, at its own power and speed."),
                Step("3", "Find the right settings", "New material? Use Tools → Material test to burn a grid of power and speed squares on a scrap, then copy the best one."),
                Step("4", "Send it", "On the Job tab, check the preview and time, then click Send to machine. Nothing happens until you press the start button on the machine."),
                Hint("Help → Getting started shows this again. Hover over any setting to see what it does."),
            },
        };
    }

    static Control Col1(Control c)
    {
        Grid.SetColumn(c, 1);
        return c;
    }
}
