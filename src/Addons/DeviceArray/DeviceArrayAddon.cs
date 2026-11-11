using System.Globalization;
using Avalonia;
using Avalonia.Media;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.DeviceArray;

/// <summary>
/// Arrange → Repeat → Device array: the same design marked on a matrix of devices (a tray of pens,
/// tags or parts) at a set pitch, each numbered ahead for its own serial or spreadsheet row, any of
/// them turned off on its own. The copies are kept in step with the first device by <see cref="ArrayRule"/>,
/// and an overlay shows the devices, their numbers and which are off while the array is selected.
/// </summary>
public sealed class DeviceArrayAddon : IAddon
{
    /// <summary>A 3 × 3 tray of devices with the middle one off (24 × 24).</summary>
    const string Icon = "F0 M2 2h5v5H2z M9.5 2h5v5h-5z M17 2h5v5h-5z M2 9.5h5v5H2z M17 9.5h5v5h-5z M2 17h5v5H2z M9.5 17h5v5h-5z M17 17h5v5h-5z "
        + "M9.5 9.5h5v5h-5z M10.5 10.5v3h3v-3z";

    IAddonHost _host = null!;
    DeviceArrayWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddDocRule(new ArrayRule());
        host.AddRibbonButton(new RibbonButton("Arrange", "Repeat", "Device array", Icon, Open)
        {
            Tip = "Mark the selection on a matrix of devices (a tray of pens, tags or parts) at a set X/Y pitch, each with its own serial number; turn any device off on its own",
            Enabled = () => _host.Document.Selection.Count > 0,
            Order = 10,
        });
        host.AddCommand(new AddonCommand("devicearray.open", "Shapes", "Device array…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Change device array…", Open) { Visible = () => Touching() is not null });
        host.AddContextMenuItem(new ContextMenuEntry("Turn device off", () => Switch(false)) { Visible = () => Devices().Any(d => d.On) });
        host.AddContextMenuItem(new ContextMenuEntry("Turn device on", () => Switch(true)) { Visible = () => Devices().Any(d => !d.On) });
        host.AddCanvasOverlay(new Overlay(this));
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
    }

    DeviceArrays.ArrayView? Touching() => DeviceArrays.Touching(_host.Document.State, _host.Document.Selection);

    /// <summary>The devices the selected shapes are in, and whether each is on.</summary>
    List<(Guid Array, Cell Cell, bool On)> Devices()
    {
        var doc = _host.Document;
        var found = new List<(Guid, Cell, bool)>();
        if (doc.Selection.Count == 0) return found;
        var specs = new Dictionary<Guid, ArraySpec?>();
        foreach (var sh in doc.State.Shapes)
        {
            if (!doc.Selection.Contains(sh.Id) || DeviceArrays.DeviceOf(sh) is not { } d) continue;
            if (!specs.TryGetValue(d.Array, out var spec)) specs[d.Array] = spec = DeviceArrays.Find(doc.State, d.Array)?.Spec;
            if (spec is null || found.Any(f => f.Item1 == d.Array && f.Item2 == d.Cell)) continue;
            found.Add((d.Array, d.Cell, spec.IsOn(d.Cell)));
        }
        return found;
    }

    /// <summary>Turn the selected devices on or off (the rest stay as they are).</summary>
    void Switch(bool on)
    {
        var doc = _host.Document;
        var s = doc.State;
        foreach (var group in Devices().GroupBy(d => d.Array))
            if (DeviceArrays.Find(s, group.Key) is { } v)
                s = DeviceArrays.SetSpec(s, group.Key, group.Aggregate(v.Spec, (spec, d) => spec.With(d.Cell, on)));
        doc.Commit(on ? "Turn device on" : "Turn device off", s);
    }

    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Touching()?.Id;
            if (editing is null && !_host.Document.State.Shapes.Any(s => _host.Document.Selection.Contains(s.Id)))
            {
                _host.Status("Device array: select the design to mark on each device first.");
                return;
            }
            _window = new DeviceArrayWindow(_host.Document, _host.Machine, editing);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; _host.InvalidateCanvas(); }
        });
    }

    /// <summary>While an array is selected: each device's place, its number, and a cross on the ones that are off.</summary>
    sealed class Overlay(DeviceArrayAddon owner) : ICanvasOverlay
    {
        static readonly IBrush Line = new SolidColorBrush(Color.FromArgb(0xB0, 0x38, 0xBD, 0xF8));
        static readonly IBrush OffLine = new SolidColorBrush(Color.FromArgb(0xD0, 0xF9, 0x73, 0x16));
        static readonly IBrush Label = new SolidColorBrush(Color.FromArgb(0xE0, 0x38, 0xBD, 0xF8));
        static readonly Typeface Face = new(FontFamily.Default);

        public CanvasLayer Layer => CanvasLayer.Screen;

        public void Draw(DrawingContext ctx, CanvasDrawInfo info)
        {
            var doc = owner._host.Document;
            if (doc.Selection.Count == 0) return;
            var shown = new HashSet<Guid>();
            foreach (var sh in doc.State.Shapes)
            {
                if (!doc.Selection.Contains(sh.Id) || DeviceArrays.DeviceOf(sh) is not { } d || !shown.Add(d.Array)) continue;
                if (DeviceArrays.Find(doc.State, d.Array) is { } view) DrawArray(ctx, info, view);
            }
        }

        static void DrawArray(DrawingContext ctx, CanvasDrawInfo info, DeviceArrays.ArrayView view)
        {
            var spec = view.Spec;
            var numbers = spec.Numbers();
            var pen = new Pen(Line, 1, new DashStyle([4, 3], 0));
            var off = new Pen(OffLine, 1.5);
            double fontSize = 11;
            foreach (var cell in spec.Cells())
            {
                var box = view.CellBox(cell);
                if (!box.Intersects(info.Visible)) continue;
                var a = info.ToScreen(box.TopLeft);
                var b = info.ToScreen(box.BottomRight);
                var r = new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                if (r.Width < 3 || r.Height < 3) continue;
                ctx.DrawRectangle(null, pen, r);
                bool on = spec.IsOn(cell);
                if (!on)
                {
                    var d = r.Deflate(Math.Min(r.Width, r.Height) * 0.15);
                    ctx.DrawLine(off, d.TopLeft, d.BottomRight);
                    ctx.DrawLine(off, d.TopRight, d.BottomLeft);
                }
                if (r.Width < 22 || r.Height < 16 || (!on && !spec.KeepNumbers)) continue;
                var ft = new FormattedText((numbers[cell] + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Face, fontSize, on ? Label : OffLine);
                ctx.DrawText(ft, new Point(r.X + 3, r.Y + 2));
            }
        }
    }
}
