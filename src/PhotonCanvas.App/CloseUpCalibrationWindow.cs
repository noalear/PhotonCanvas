using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Teach PhotonCanvas where the close-up camera looks: ten small dots burned where it can see
/// them, found in a close-up photo. After that, close-up photos land on the bed by themselves
/// (sharp results for the tone and line tests), at this focus height.
/// </summary>
public sealed class CloseUpCalibrationWindow : Window
{
    readonly Document _doc;
    readonly MachinePanel _machine;
    readonly MachineProfile _profile;

    readonly NumericUpDown _spacing = Number(2, 10, 0.5m, "0.#"), _dot = Number(0.5m, 3, 0.1m, "0.0#");
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _focus = new() { TextWrapping = TextWrapping.Wrap };
    readonly Image _look = new() { Stretch = Stretch.Uniform, Height = 220, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _lookNote = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    readonly MarksView _view = new() { Height = 360 };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly Button _save = new() { Content = "Save where the close-up camera looks", Classes = { "primary" }, IsEnabled = false };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    CloseUpMarks.Located? _found;

    public CloseUpCalibrationWindow(Document doc, MachinePanel machine, MachineProfile profile, bool find)
    {
        _doc = doc;
        _machine = machine;
        _profile = profile;
        Title = "Close-up camera position";
        Width = 820;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _spacing.Value = 4;
        _dot.Value = 1.2m;
        _spacing.ValueChanged += (_, _) => Plan();
        _dot.ValueChanged += (_, _) => Plan();

        var look = Button("Take a look", "IcZoomIn", "A close-up photo, to see where the camera looks.");
        look.Click += async (_, _) => await Look(look);
        var add = new Button { Content = "Add the dots to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => AddMarks();
        var burn = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "The close-up camera in the laser head sees a small part of the bed, sharply, and always the same part at a given focus height. " +
                           "PhotonCanvas burns ten small dots where it can see them, finds them in a close-up photo, and from then on knows exactly where it looks: " +
                           "tone and line tests are read from close-up photos instead of the overview camera.",
                },
                _focus,
                Explained("1. Put scrap under the camera", "Flat, at the height you'll use (the position is kept per focus height). Take a look to see what the close-up camera sees.", Row(look), _look, _lookNote),
                Explained("2. The dots", "Ten dots: a 3 × 3 grid and one off to the side. They must all fit in the close-up picture, with room to spare.",
                    Row(Labelled("Apart (mm)", _spacing), Labelled("Dot size (mm)", _dot))),
                _plan,
                add,
                Hint("Adding the dots closes this window. Frame them and watch the close-up view (Camera → Close-up, live): the red light should trace them well inside the picture. " +
                     "Move them until it does, set the \"Close-up marks\" layer's power so they come out dark on your material, and send. When the job finishes, this window opens again on Find the dots."),
            },
        };

        var take = Button("Take a close-up photo", "IcCamera", "Photograph the burned dots with the close-up camera and find them.");
        take.Click += async (_, _) => await Find(take);
        _save.Click += (_, _) => Save();
        var remove = Button("Remove the dots from the design", "IcTrash");
        remove.Click += (_, _) => { _doc.Commit("Remove close-up marks", CloseUpMarks.Remove(_doc.State)); _status.Text = "The dots are out of the design."; };
        var findTab = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                Hint("Don't move the scrap after burning: the dots are found where they were burned."),
                Row(take),
                _view,
                _result,
                _save,
                _status,
                Row(remove),
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Burn the dots", Content = new ScrollViewer { Content = burn } }, new TabItem { Header = "Find the dots", Content = new ScrollViewer { Content = findTab } } } };
        tabs.SelectedIndex = find ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        Plan();
        _focus.Text = _machine.Distance is { } d
            ? $"Focus height now: {d:0.0} mm." + (_machine.CloseUpSees() is { } sees ? $" The close-up camera's position is already known at this height (it sees {sees.Width:0} × {sees.Height:0} mm); doing it again replaces it." : "")
            : "Connect the machine first: the position is kept per focus height, and the photo comes from the machine.";
    }

    /// <summary>Are there dots in the design waiting to be found?</summary>
    public static bool InDesign(DocState s) => CloseUpMarks.Marks(s) is not null;

    void Plan()
    {
        double sp = (double)(_spacing.Value ?? 4), dot = (double)(_dot.Value ?? 1.2m);
        _plan.Text = $"Ten dots {dot:0.0#} mm across, {sp:0.#} mm apart: {2 * sp + dot:0.#} × {2.6 * sp + dot:0.#} mm.";
    }

    async Task Look(Button button)
    {
        button.IsEnabled = false;
        try
        {
            if (!_machine.IsConnected) { _lookNote.Text = "Connect the machine first."; return; }
            if (await _machine.CloseUpPhotoAsync() is not { } jpg) return;
            var img = await Task.Run(() => RasterImage.Decode(jpg).Rotated(_machine.CloseUpRotationDeg));
            using var ms = new MemoryStream(img.EncodePng());
            _look.Source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 800);
            _lookNote.Text = "This is what the close-up camera sees. Put the scrap so it fills this picture.";
        }
        catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or IOException or FormatException)
        {
            _lookNote.Text = $"Couldn't take a photo: {e.Message}";
        }
        finally { button.IsEnabled = true; }
    }

    void AddMarks()
    {
        double sp = (double)(_spacing.Value ?? 4), dot = (double)(_dot.Value ?? 1.2m);
        // Where it looked before (another focus height), else the middle of the bed.
        var at = _machine.CloseUpSeesAnyHeight() is { } before ? before.Center : new Vec2(_profile.FieldW / 2, _profile.FieldH / 2);
        var (s, ids) = CloseUpMarks.Add(_doc.State, at, sp, dot);
        _doc.Commit("Close-up marks", s, ids);
        AppLog.Write("close-up", $"Marks added at ({at.X:0.#}, {at.Y:0.#}), {sp:0.#} mm apart.");
        Close();
    }

    async Task Find(Button button)
    {
        _found = null;
        _save.IsEnabled = false;
        button.IsEnabled = false;
        try
        {
            if (CloseUpMarks.Marks(_doc.State) is not { } marks) { _result.Text = "There are no close-up dots in this design: add them on the first tab."; return; }
            if (!_machine.IsConnected || _machine.Distance is null) { _result.Text = "Connect the machine first."; return; }
            _result.Text = "Taking a close-up photo…";
            if (await _machine.CloseUpPhotoAsync() is not { } jpg) return;
            var (raw, found, spots) = await Task.Run(() =>
            {
                var r = RasterImage.Decode(jpg);
                return (r, CloseUpMarks.Locate(r, marks), CloseUpMarks.FindSpots(r));
            });
            _view.Show(raw, spots, found);
            if (found is null)
            {
                _result.Text = $"Couldn't find the dots: {spots.Count} dark spot{(spots.Count == 1 ? "" : "s")} in the photo, but not the pattern " +
                               "(at least seven dots, and the odd one off the side). Check they all burned dark and are inside the picture, then try again.";
                return;
            }
            _found = found;
            int n = found.Found.Count(f => f is not null);
            _result.Text = $"Found {n} of {found.Found.Count} dots; they line up to within {found.ErrorMm:0.00} mm. " +
                           $"The close-up camera sees {found.Sees.Width:0} × {found.Sees.Height:0} mm of the bed, from ({found.Sees.MinX:0}, {found.Sees.MinY:0}) to ({found.Sees.MaxX:0}, {found.Sees.MaxY:0}), at {_machine.Distance:0.0} mm focus.";
            _save.IsEnabled = found.ErrorMm < 0.2;
            if (!_save.IsEnabled) _result.Text += " That's too far off to trust: check the scrap didn't move, and try again.";
        }
        catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or IOException or FormatException)
        {
            _result.Text = $"Couldn't take a photo: {e.Message}";
        }
        finally { button.IsEnabled = true; }
    }

    void Save()
    {
        if (_found is not { } f || _machine.Distance is not { } d) return;
        _machine.SaveCloseUpPlacement(d, f.MmToCamera);
        // The camera's own turn, so the close-up view comes out straight too (not for a mirrored picture).
        var mid = f.Sees.Center;
        Vec2 a = f.MmToCamera.Apply(mid), bx = f.MmToCamera.Apply(mid + new Vec2(1, 0)), by = f.MmToCamera.Apply(mid + new Vec2(0, 1));
        var ux = bx - a;
        var uy = by - a;
        bool mirrored = ux.X * uy.Y - ux.Y * uy.X < 0;
        if (!mirrored) _machine.LearnCloseUpRotation(-Math.Atan2(ux.Y, ux.X) * 180 / Math.PI);
        AppLog.Write("close-up", $"Position saved at {d:0.0} mm focus: sees {f.Sees}, error {f.ErrorMm:0.000} mm.");
        _status.Text = $"Saved for {d:0.0} mm focus. Tone and line tests can now be read from close-up photos, and placed where the camera sees them. You can remove the dots from the design.";
        _status.Foreground = Brush("Ok");
    }

    /// <summary>The close-up photo with every dark spot ringed, and the dots that were matched numbered.</summary>
    sealed class MarksView : Control
    {
        Avalonia.Media.Imaging.Bitmap? _photo;
        (int W, int H) _size;
        List<CloseUpMarks.Spot> _spots = [];
        CloseUpMarks.Located? _found;

        public void Show(RasterImage raw, List<CloseUpMarks.Spot> spots, CloseUpMarks.Located? found)
        {
            using var ms = new MemoryStream(raw.EncodePng());
            _photo = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 1000);
            _size = (raw.Width, raw.Height);
            (_spots, _found) = (spots, found);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            if (_photo is null) return;
            double k = Math.Min(Bounds.Width / _size.W, Bounds.Height / _size.H);
            ctx.DrawImage(_photo, new Rect(0, 0, _size.W * k, _size.H * k));
            Point P(Vec2 v) => new(v.X * k, v.Y * k);
            var faint = new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 200, 0)), 1.5);
            foreach (var s in _spots) ctx.DrawEllipse(null, faint, P(s.At), 7, 7);
            if (_found is null) return;
            var ok = new Pen(Brushes.LimeGreen, 2.5);
            for (int i = 0; i < _found.Found.Count; i++)
            {
                if (_found.Found[i] is not { } at) continue;
                ctx.DrawEllipse(null, ok, P(at), 10, 10);
                var t = new FormattedText($"{i + 1}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 13, Brushes.LimeGreen);
                ctx.DrawText(t, P(at) + new Point(12, -8));
            }
        }
    }
}
