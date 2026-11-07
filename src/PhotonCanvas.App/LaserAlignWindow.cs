using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Lining the MOPA up with the blue laser: both burn a grid of dots where the close-up camera
/// sees, the camera measures where each lands, and the difference is kept: MOPA work is then moved
/// back by it in every job.
/// </summary>
public sealed class LaserAlignWindow : Window
{
    readonly Document _doc;
    readonly MachinePanel _machine;
    readonly AppSettings _settings;
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _save = new() { Content = "Use this alignment", Classes = { "primary" }, IsEnabled = false };
    LaserAlign.Result? _found;

    public LaserAlignWindow(Document doc, MachinePanel machine, AppSettings settings, bool find)
    {
        _doc = doc;
        _machine = machine;
        _settings = settings;
        Title = "Align MOPA to blue";
        Width = 620;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var add = new Button { Content = "Add the dots to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => Add();
        bool known = machine.CloseUpSees() is not null;
        var burn = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "The blue laser and the MOPA (fiber) laser each burn a grid of nine small dots, the MOPA's 18 mm below the blue's, where the close-up camera sees. The camera then finds every dot: how far apart the two lasers land is kept, and MOPA work is moved to match." },
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = known ? "The close-up camera's position is known at this focus height." : "First teach PhotonCanvas where the close-up camera looks (Machine → Close-up position), at this focus height.", Foreground = Brush(known ? "Ok" : "Warn") },
                Hint(settings.MopaOffsetX == 0 && settings.MopaOffsetY == 0 ? "Now in use: none (MOPA work isn't moved)."
                    : $"Now in use: the MOPA lands {settings.MopaOffsetX:+0.00;-0.00;0} mm across and {settings.MopaOffsetY:+0.00;-0.00;0} mm down from the blue laser, so MOPA work is moved back by that much."),
                add,
                Hint("Use scrap both lasers mark (anodised aluminium, black card, coated metal, or wood). Set each layer's power so its dots come out dark, then send. When the job finishes this window opens again."),
            },
        };
        add.IsEnabled = known;
        var take = Button("Take a close-up photo", "IcCamera", "Photograph the dots and measure where each laser landed.");
        take.Click += async (_, _) => await Measure(take);
        _save.Click += (_, _) => Save();
        var remove = Button("Remove the dots from the design", "IcTrash");
        remove.Click += (_, _) => { _doc.Commit("Remove alignment dots", LaserAlign.Remove(_doc.State)); _status.Text = "The dots are out of the design."; };
        var reset = Button("Back to no correction");
        reset.Click += (_, _) => { _settings.MopaOffsetX = _settings.MopaOffsetY = 0; _settings.Save(); _status.Text = "MOPA work is no longer moved."; };
        var measure = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children = { Hint("Don't move the scrap after burning."), Row(take), new LightControls(() => _machine.Lights, _machine.SetLights), _result, _save, _status, Row(remove, reset) },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Burn the dots", Content = new ScrollViewer { Content = burn } }, new TabItem { Header = "Measure", Content = new ScrollViewer { Content = measure } } } };
        tabs.SelectedIndex = find ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
    }

    public static bool InDesign(DocState s) => LaserAlign.Marks(s) is not null;

    void Add()
    {
        if (_machine.CloseUpSees() is not { } sees) return;
        var centre = new Vec2(sees.Center.X, sees.Center.Y - LaserAlign.Apart / 2);
        var s = LaserAlign.Add(_doc.State, centre, 6, 1.2, LaserSettings.Blue(60, 100), LaserSettings.Red(60, 500, 200, 60));
        _doc.Commit("Alignment dots", s);
        AppLog.Write("align", "Alignment dots added.");
        Close();
    }

    async Task Measure(Button button)
    {
        _found = null;
        _save.IsEnabled = false;
        button.IsEnabled = false;
        try
        {
            if (LaserAlign.Marks(_doc.State) is not { } marks) { _result.Text = "There are no alignment dots in this design: add them on the first tab."; return; }
            if (_machine.CloseUpPlacement() is not { } toCamera) { _result.Text = "The close-up camera's position isn't known at this focus height."; return; }
            if (await _machine.CloseUpPhotoAsync() is not { } jpg) { _result.Text = "Couldn't take a close-up photo: is the machine connected?"; return; }
            var r = await Task.Run(() => LaserAlign.Measure(RasterImage.Decode(jpg), toCamera, marks.Blue, marks.Mopa));
            if (r is null) { _result.Text = "Couldn't find enough dots of both grids (five each). Check they burned dark and are inside the close-up picture."; return; }
            _found = r;
            var d = r.MopaFromBlue;
            _result.Text = $"Found {r.BlueFound} blue and {r.MopaFound} MOPA dots (agreeing within {r.SpreadMm:0.00} mm). " +
                           $"The MOPA lands {Math.Abs(d.X):0.00} mm {(d.X >= 0 ? "right" : "left")} and {Math.Abs(d.Y):0.00} mm {(d.Y >= 0 ? "down" : "up")} of the blue laser" +
                           (_settings.MopaOffsetX != 0 || _settings.MopaOffsetY != 0 ? " (with the correction now in use)." : ".");
            _save.IsEnabled = r.SpreadMm < 0.4;
        }
        catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or IOException or FormatException) { _result.Text = $"Couldn't measure: {e.Message}"; }
        finally { button.IsEnabled = true; }
    }

    void Save()
    {
        if (_found is not { } r) return;
        // Measured with whatever correction was in use: the new one adds to it.
        _settings.MopaOffsetX = Math.Round(_settings.MopaOffsetX + r.MopaFromBlue.X, 3);
        _settings.MopaOffsetY = Math.Round(_settings.MopaOffsetY + r.MopaFromBlue.Y, 3);
        _settings.Save();
        AppLog.Write("align", $"MOPA offset now {_settings.MopaOffsetX:0.000}, {_settings.MopaOffsetY:0.000} mm.");
        _status.Text = $"From now on MOPA work is moved {-_settings.MopaOffsetX:+0.00;-0.00;0} mm across and {-_settings.MopaOffsetY:+0.00;-0.00;0} mm down to land where the blue laser does. Burn the dots again to check.";
        _status.Foreground = Brush("Ok");
    }
}
