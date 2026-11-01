using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.CameraPlacement;

/// <summary>Camera → Place: Cut round prints (print-then-cut) and Every piece (a copy of the design on each piece the camera finds).</summary>
public sealed class CameraPlacementAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Camera", "Place", "Cut round prints", "IcCut", () => _ = host.Guard(CutRoundPrints))
        {
            Tip = "Print-then-cut: the camera finds printed art on paper (stickers, labels) and a cut goes round each one, a margin outside. Select a shape first to look only inside it",
            Large = true,
            Order = 10,
        });
        host.AddRibbonButton(new RibbonButton("Camera", "Place", "Every piece", "IcGrid", () => _ = host.Guard(PlaceOnEveryPiece))
        {
            Tip = "Photograph the bed, find each piece on it and put a copy of the selected design on every one (with the next serial number or spreadsheet row on each)",
            Large = true,
            Enabled = () => SelectedOutline().Count > 0,
            Order = 30,
        });
    }

    public void Stop() { }

    Document Doc => _host.Document;

    /// <summary>The selected shapes' outlines in work-area mm (pictures left out).</summary>
    List<IReadOnlyList<Vec2>> SelectedOutline() =>
        Doc.State.Shapes.Where(s => Doc.Selection.Contains(s.Id) && s is not ImageShape).OfType<PathShape>()
            .SelectMany(s => s.World.Flatten(0.05))
            .Where(f => f.Points.Count > 1)
            .Select(f => (IReadOnlyList<Vec2>)(f.Closed ? [.. f.Points, f.Points[0]] : f.Points))
            .ToList();

    /// <summary>Print-then-cut: cuts round printed art the camera finds (close-up when it covers the area, else the overview).</summary>
    async Task CutRoundPrints()
    {
        var owner = _host.Owner;
        if (!_host.IsConnected) { await Dialogs.Message(owner, "Cut round prints", "Connect the machine first: this needs a fresh camera photo."); return; }
        // Where to look: inside the selected shape(s), else the whole bed.
        var sel = Doc.State.Shapes.Where(x => Doc.Selection.Contains(x.Id)).Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        var within = sel.IsEmpty ? new Rect2(0, 0, _host.Machine.FieldW, _host.Machine.FieldH) : sel;
        var text = await Dialogs.Prompt(owner, "Cut round prints", "How far outside the printed art the cut goes (mm): a white border round each sticker.", "1");
        if (text is null || !double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double margin) || margin < 0) return;
        _host.Status("Photographing the prints…");
        CameraPhoto? photo = null;
        if (_host.CloseUpSees() is { } sees && sees.Contains(within.TopLeft) && sees.Contains(within.BottomRight)) photo = await _host.CloseUpPhotoAsync();
        photo ??= await _host.TakePhotoAsync();
        if (photo is null) { _host.Status("No camera photo: calibrate the camera first (Camera → Calibrate)."); return; }
        var cuts = await Task.Run(() => PrintCut.Find(photo.Image, photo.Area, photo.MmPerPx, within, new PrintCutOptions { MarginMm = margin }));
        int n = cuts.Subpaths.Length;
        if (n == 0) { _host.Status("No print found there: is it on white paper, in the camera's view and well lit?"); return; }
        var s = Doc.State;
        var layer = s.Layers.FirstOrDefault(l => l.Name == "Print cut") ?? Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = "Print cut" };
        if (s.FindLayer(layer.Id) is null) s = Ops.AddLayer(s, layer);
        var shapes = cuts.Subpaths.Select((sp, i) => (Shape)new PathShape { Name = $"Print {i + 1}", LayerId = layer.Id, Geometry = new PathGeometry([sp]) }).ToList();
        Doc.Commit("Cut round prints", Ops.AddShapes(s, shapes), shapes.Select(x => x.Id));
        _host.Status($"{n} print{(n == 1 ? "" : "s")} found: a cut round each, {margin:0.##} mm out, on the \"Print cut\" layer. Set its power for the paper (a kiss cut for stickers).");
    }

    async Task PlaceOnEveryPiece()
    {
        var owner = _host.Owner;
        var outline = SelectedOutline();
        if (outline.Count == 0) { await Dialogs.Message(owner, "Every piece", "Select the design to put on each piece first."); return; }
        if (!_host.IsConnected) { await Dialogs.Message(owner, "Every piece", "Connect to the machine first: finding the pieces needs a fresh camera photo."); return; }
        var ids = Doc.Selection;
        var w = new BatchPlaceWindow(_host.MachinePanel, outline, placements =>
        {
            // One copy per piece (each its own group); the copies' fields count up in reading order.
            var originals = Doc.State.Shapes.Where(s => ids.Contains(s.Id)).ToList();
            var made = new List<Shape>();
            var s = Doc.State;
            for (int i = 0; i < placements.Count; i++)
            {
                var copy = Ops.CopyAsUnit(originals, placements[i].Move).Select(c => c.WithVarOffset(i)).ToList();
                s = Ops.AddShapes(s, copy);
                if (copy.Count > 1) s = Ops.Group(s, copy.Select(c => c.Id).ToHashSet()).Item1;
                made.AddRange(copy);
            }
            s = Ops.Delete(s, ids);
            Doc.Commit("Place on every piece", s, made.Select(m => m.Id));
            _host.Status($"A copy on each of {placements.Count} pieces." + (VariableText.Used(Doc.State).Count > 0 ? " Each one marks the next serial number / spreadsheet row." : ""));
        });
        await w.ShowDialog(owner);
    }
}
