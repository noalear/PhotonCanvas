using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.TouchUp;

/// <summary>
/// Burn, look, touch up: take a photo of a burned engraving (close-up, or the overview camera for
/// bigger work), see it beside the design with the too-light parts in red, and add a touch-up page
/// that goes over only those parts. The touch-up is sent with Ship It and started on the machine
/// like any job; this window never moves the head or fires the laser.
/// </summary>
public sealed class TouchUpWindow : Window
{
    const string LightsKey = "touchup.lights";

    readonly IAddonHost _host;
    readonly StackPanel _layerList = new() { Spacing = 4 };
    readonly List<(CheckBox Box, Layer Layer)> _layerBoxes = [];
    readonly TextBlock _designNote = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _viewNote = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly TextBlock _photoNote = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _closeUp = Button("Close-up photo", "IcZoomIn", "Photograph the engraving with the close-up camera in the head: sharp enough to see single dots and lines. Needs the close-up camera's position at this focus height (Machine → Close-up position).");
    readonly Button _overview = Button("Overview photo", "IcCamera", "Photograph the whole bed with the overview camera: for engravings bigger than the close-up camera sees (coarser, so only bigger light patches are found).");
    readonly Button _sameLights = new() { IsVisible = false };
    readonly Button _add = new() { Content = "Add the touch-up page", Classes = { "primary" }, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _back = new() { Content = "Back to the design page", IsVisible = false };
    readonly NumericUpDown _sensitivity = Number(2, 40, 1, "0"), _patch = Number(0, 50, 0.5m, "0.#"), _strength = Number(10, 100, 5, "0"), _aim = Number(0, 60, 5, "0");
    readonly Image _wanted = new() { Stretch = Stretch.Uniform, Height = 200 }, _found = new() { Stretch = Stretch.Uniform, Height = 200 }, _light = new() { Stretch = Stretch.Uniform, Height = 200 };
    readonly Control _pictures;
    readonly LightControls? _lights;
    CameraPhoto? _photo;
    string _photoKind = "";
    TouchUpResult? _result;
    DocState? _design;
    int _run;

    public TouchUpWindow(IAddonHost host)
    {
        _host = host;
        Title = "Touch up";
        Width = 900;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var defaults = new TouchUpOptions();
        _sensitivity.Value = (decimal)(defaults.Sensitivity * 100);
        _patch.Value = (decimal)defaults.MinPatchMm2;
        _strength.Value = (decimal)(defaults.Strength * 100);
        _aim.Value = (decimal)(defaults.AimDarker * 100);
        foreach (var n in new[] { _sensitivity, _patch, _strength, _aim }) n.ValueChanged += (_, _) => _ = CompareAsync();

        if (host.IsConnected) _lights = new LightControls(() => host.Lights, host.SetLights);
        _closeUp.Click += async (_, _) => await TakeAsync(closeUp: true);
        _overview.Click += async (_, _) => await TakeAsync(closeUp: false);
        _sameLights.Click += (_, _) => { if (StoredLights() is { } l) { host.SetLights(l.Front, l.Back); _lights?.Refresh(); ShowLights(); } };
        _add.Click += (_, _) => AddPage();
        _back.Click += (_, _) => BackToDesign();

        var grid = new Grid { ColumnDefinitions = new("*,8,*,8,*") };
        void Put(Control c, int col) { Grid.SetColumn(c, col); grid.Children.Add(c); }
        Put(Labelled("The design", Framed(_wanted)), 0);
        Put(Labelled("The photo, lined up", Framed(_found)), 2);
        Put(Labelled("Too light (red)", Framed(_light)), 4);
        _pictures = grid;
        _pictures.IsVisible = false;

        var content = new StackPanel
        {
            Spacing = 14, Margin = new(20),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Photograph an engraving after it's burned. Its darkness is compared with the design, spot by spot, and the parts that came out too light " +
                           "go on a touch-up page of their own, with the same layer settings. Send that with Ship It and start it on the machine as usual, then look again.",
                },
                Explained("What to check", "The design's engraving and picture layers. Cut and score lines, and relief (depth slices), can't be judged from a photo.",
                    _designNote, _layerList, _back),
                Explained("Photo", "Keep the piece where it was burned. The light matters: no glare on the burn, and the same lights each round.",
                    _viewNote, Row(_closeUp, _overview), (Control?)_lights ?? new Panel(), _sameLights, _photoNote),
                _pictures,
                _summary,
                _details,
                Explained("How it judges", "What each grey should come out as is taken from the best-burned parts of that grey in this same burn, so it works on any material. " +
                                           "Light patches are found against the bare material right beside them, so uneven light doesn't count.",
                    Row(Labelled("Ignore differences under (%)", _sensitivity), Labelled("Smallest patch (mm²)", _patch)),
                    Row(Labelled("Strength (%)", _strength), Labelled("Aim darker (%)", _aim)),
                    Hint("Strength: how much of the missing darkness the touch-up puts back (pictures: as more dots; engraved areas: as power). Below 100% is safer: darkness can be added, never taken away. " +
                         "Aim darker: also darken the whole engraving past the best of this burn.")),
                _add,
                Hint("Adding the page shows it, so Ship It sends just the touch-up. The design page stays as it was. After it's burned, open Touch up again to look once more."),
            },
        };
        Content = new ScrollViewer { Content = content };
        ToolTip.SetTip(_sensitivity, "Spots less than this much short of the darkness they should have are left alone (the photo's noise, wood grain, the dots themselves). Share of the darkest tone's darkness.");
        ToolTip.SetTip(_patch, "Light patches smaller than this are left alone.");
        ToolTip.SetTip(_strength, "How much of the missing darkness the touch-up puts back. 100% = all of it; lower is safer, and you can always do another round.");
        ToolTip.SetTip(_aim, "0 = even the engraving up to its own best-burned parts. Higher = aim this much darker than those, so the whole engraving is darkened too.");
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { host.ShowHelp("touch-up.md"); e.Handled = true; } };

        LoadDesign();
        ShowLights();
        ShowView();
        _photoNote.Text = "Take a photo of the burned engraving to compare it with the design.";
    }

    static Border Framed(Control c) => new() { Background = Brush("Bg0"), CornerRadius = new(6), Padding = new(4), Child = c };

    /// <summary>The design to compare with, and its layers to choose from.</summary>
    void LoadDesign()
    {
        var s = _host.Document.State;
        _design = TouchUpPage.DesignOf(s);
        _layerList.Children.Clear();
        _layerBoxes.Clear();
        if (_design is null)
        {
            _designNote.Text = "There's no design page to compare with: every page is a touch-up page.";
            return;
        }
        bool onTouchUp = TouchUpPage.IsTouchUpPage(s.PageName);
        _back.IsVisible = onTouchUp;
        _designNote.Text = onTouchUp
            ? $"Compared with the design on page \"{_design.PageName}\" (you're on \"{s.PageName}\"; its own layers aren't compared)."
            : $"Compared with the design on page \"{_design.PageName}\".";
        foreach (var l in TouchUpAnalysis.Candidates(_design))
        {
            var box = new CheckBox { IsChecked = true, Content = $"{l.Name}  ({(l.Mode == LayerMode.Image ? "picture" : "engraved area")})" };
            box.IsCheckedChanged += (_, _) => { ShowView(); _ = CompareAsync(); };
            _layerBoxes.Add((box, l));
            _layerList.Children.Add(box);
        }
        if (_layerBoxes.Count == 0) _designNote.Text += " It has no engraving or picture layers to check.";
    }

    List<Layer> Chosen() => [.. _layerBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Layer)];

    /// <summary>Whether the close-up camera sees what's to be checked.</summary>
    void ShowView()
    {
        bool on = _host.IsConnected;
        var sees = _host.CloseUpSees();
        _closeUp.IsEnabled = on && sees is not null;
        _overview.IsEnabled = on || _host.Photo is not null;
        if (!on)
        {
            _viewNote.Text = _host.Photo is not null
                ? "The machine isn't connected: the last overview photo can be used, or connect for a close-up."
                : "Connect the machine to take a photo.";
            return;
        }
        if (_design is null || TouchUpAnalysis.Extent(_design, Chosen()) is not { IsEmpty: false } ext) { _viewNote.Text = ""; return; }
        if (sees is not { } v) _viewNote.Text = "Where the close-up camera looks isn't known at this focus height (Machine → Close-up position), so only the overview photo can be used.";
        else if (v.Contains(ext)) _viewNote.Text = $"The close-up camera sees all of it ({ext.Width:0.#} × {ext.Height:0.#} mm): use the close-up photo.";
        else if (v.Intersects(ext)) _viewNote.Text = $"The close-up camera sees only part of it ({v.Width:0} × {v.Height:0} mm of the {ext.Width:0} × {ext.Height:0} mm): that part is checked. Use the overview photo for all of it, more coarsely.";
        else _viewNote.Text = "The close-up camera doesn't see the engraving from where the head is: use the overview photo.";
    }

    (int Front, int Back)? StoredLights()
    {
        if (!_host.Document.State.AddonData.TryGetValue(LightsKey, out var v)) return null;
        var parts = v.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int f) && int.TryParse(parts[1], out int b) ? (f, b) : null;
    }

    void ShowLights()
    {
        _sameLights.IsVisible = _host.IsConnected && StoredLights() is { } l && l != _host.Lights;
        if (StoredLights() is { } s) _sameLights.Content = $"Use the lights from the last round (front {s.Front}, back {s.Back})";
    }

    async Task TakeAsync(bool closeUp)
    {
        _closeUp.IsEnabled = _overview.IsEnabled = false;
        _photoNote.Text = "Taking a photo…";
        try
        {
            _photo = closeUp ? await _host.CloseUpPhotoAsync() : _host.IsConnected ? await _host.TakePhotoAsync() : _host.Photo;
            _photoKind = closeUp ? "close-up" : "overview";
            if (_photo is null)
            {
                _photoNote.Text = closeUp
                    ? "Couldn't take a close-up photo: connect the machine, at the focus height the close-up position was saved for."
                    : "Couldn't take a photo: connect the machine and calibrate the camera.";
                return;
            }
            await CompareAsync();
        }
        catch (Exception e) { _photoNote.Text = $"Couldn't take a photo: {e.Message}"; }
        finally { ShowView(); }
    }

    TouchUpOptions Options() => new()
    {
        Sensitivity = (double)(_sensitivity.Value ?? 8) / 100,
        MinPatchMm2 = (double)(_patch.Value ?? 0.5m),
        Strength = (double)(_strength.Value ?? 70) / 100,
        AimDarker = (double)(_aim.Value ?? 0) / 100,
        // The overview camera can be further off than the close-up.
        MaxShiftMm = _photoKind == "overview" ? 2 : 1,
    };

    async Task CompareAsync()
    {
        if (_photo is not { } photo || _design is not { } design) return;
        int run = ++_run;
        var layers = Chosen();
        var o = Options();
        _photoNote.Text = $"Comparing the {_photoKind} photo with the design…";
        try
        {
            var (r, pics) = await Task.Run(() =>
            {
                var res = TouchUpAnalysis.Compare(design, layers, photo.Image, photo.Area, photo.MmPerPx, o);
                return (res, TouchUpPage.Pictures(res));
            });
            if (run != _run) return;
            _result = r;
            _wanted.Source = ToBitmap(pics.Wanted);
            _found.Source = ToBitmap(pics.Found);
            _light.Source = ToBitmap(pics.TooLight);
            _pictures.IsVisible = true;
            _summary.Text = r.Summary;
            _details.Text = Details(r);
            _photoNote.Text = $"From the {_photoKind} photo ({r.Grid.Area.Width:0.#} × {r.Grid.Area.Height:0.#} mm looked at, every {r.Grid.Pitch:0.##} mm). " +
                              (r.ShiftMm.Length > 0.05 ? $"The photo sat {r.ShiftMm.Length:0.##} mm off the design and was lined up." : "It lined up with the design.");
            _add.IsEnabled = r.NeedsTouchUp;
        }
        catch (TouchUpException e)
        {
            if (run != _run) return;
            _result = null;
            _pictures.IsVisible = false;
            _summary.Text = "";
            _details.Text = "";
            _photoNote.Text = e.Message;
            _add.IsEnabled = false;
        }
    }

    static string Details(TouchUpResult r)
    {
        var lines = new List<string>();
        foreach (var l in r.Layers)
        {
            if (l.Note is { } note) lines.Add($"{l.Layer.Name}: {note}");
            else if (l.TooLight == 0) lines.Add($"{l.Layer.Name}: as dark as it should be.");
            else lines.Add($"{l.Layer.Name}: {l.TooLightShare * 100:0.#}% too light, on average {l.MeanDeficit * 100:0}% short of its darkness (at worst {l.WorstDeficit * 100:0}%).");
        }
        if (r.MarksLighter) lines.Add("The marks come out lighter than the material here, so \"too light\" means not light enough.");
        return string.Join("\n", lines);
    }

    void AddPage()
    {
        if (_result is not { } r || !r.NeedsTouchUp) return;
        var s = _host.Document.State;
        int round = TouchUpPage.NextRound(s);
        if (TouchUpPage.Add(s, r, Options(), round) is not { } next) return;
        // Remember the lights, so the next look is taken the same way.
        if (_host.IsConnected)
        {
            var (f, b) = _host.Lights;
            next = next with { AddonData = next.AddonData.SetItem(LightsKey, $"{f},{b}") };
        }
        _host.Document.Commit($"Touch-up {round}", next);
        _host.Status($"Touch-up {round} is on a page of its own. Send it with Ship It and start it on the machine, then open Touch up again to look once more.");
        Close();
    }

    void BackToDesign()
    {
        var s = _host.Document.State;
        if (TouchUpPage.DesignOf(s) is not { } design || design.PageIndex == s.PageIndex) return;
        _host.Document.Commit("Back to the design", Pages.SwitchTo(s, design.PageIndex));
        LoadDesign();
    }

    static WriteableBitmap ToBitmap(RasterImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = wb.Lock();
        int row = img.Width * 4;
        for (int y = 0; y < img.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(img.Rgba, y * row, fb.Address + y * fb.RowBytes, row);
        return wb;
    }
}
