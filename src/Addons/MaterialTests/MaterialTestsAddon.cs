using Avalonia.Controls;
using Avalonia.Threading;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.MaterialTests;

/// <summary>
/// Laser → Tests: etch rate, cut test (kerf and slot fit), exact depth, tone, line, bevel and
/// graphene tests, and Inlay (built on the etch and cut results). A burned test opens its results
/// when the job finishes.
/// </summary>
public sealed class MaterialTestsAddon : IAddon
{
    IAddonHost _host = null!;
    readonly List<Window> _open = [];

    public void Start(IAddonHost host)
    {
        _host = host;
        Add(new RibbonButton("Laser", "Tests", "Etch rate", "IcDepth", () => _ = host.Guard(() => Etch(HasEtch())))
        {
            Large = true,
            Tip = "How deep one pass goes: circles burned 1, 2, 3 … passes until one goes right through; saved with the material",
        });
        Add(new RibbonButton("Laser", "Tests", "Cut test", "IcTestGrid", () => _ = host.Guard(() => Cut(CutTestWindow.InDesign(Doc.State))))
        {
            Large = true,
            Tip = "Find the cut: small squares at different speeds and passes, see which drop out; then the kerf and the slot fit at that setting; saved with the material",
        });
        Add(new RibbonButton("Laser", "Tests", "Exact depth", "IcDepth", () => _ = host.Guard(ExactDepth))
        {
            Large = true,
            Tip = "Engrave the selected shapes to an exact depth: the red pointer measures the surface, then the depth after each round of passes, and works out the next round",
        });
        Add(new RibbonButton("Laser", "Tests", "Tone test", "IcPalette", () => _ = host.Guard(() => Tone(ToneTestWindow.InDesign(Doc.State))))
        {
            Large = true,
            Tip = "Photos that come out as they look: grey patches burned with a picture layer's settings, read by the camera, and that layer's pictures corrected for the material",
        });
        Add(new RibbonButton("Laser", "Tests", "Line test", "IcLine", () => _ = host.Guard(() => Line(LineTestWindow.InDesign(Doc.State))))
        {
            Large = true,
            Tip = "Hide the rows pictures are burned in: the same grey patch burned with a picture layer's settings and with one change each (spread, direction, closer lines), side by side",
        });
        Add(new RibbonButton("Laser", "Tests", "Inlay", "IcLayers", () => _ = host.Guard(Inlay))
        {
            Tip = "The selected shapes as an inlay: a pocket engraved as deep as the veneer (from your etch test) and the piece cut to fit (with your cut test's kerf)",
            Enabled = () => SelectedPaths().Count > 0,
        });
        Add(new RibbonButton("Laser", "Tests", "Bevel test", "IcMeasure", () => _ = host.Guard(() => Bevel(PhotonCanvas.Doc.Bevel.InDesign(Doc.State))))
        {
            Tip = "How much the galvo's beam slants away from the middle of the field: four bars cut through a board and measured with calipers. Needed for bevelled edges",
        });
        Add(new RibbonButton("Laser", "Tests", "Graphene test", "IcBolt", () => _ = host.Guard(() => Graphene(GrapheneTestWindow.InDesign(Doc.State))))
        {
            Tip = "Experimental: laser-induced graphene (conductive carbon burned into cork or wood). Strips at different power and speed, measured with a multimeter",
        });

        // Tone and line tests are read from the close-up camera: show where it looks while one is in the design.
        host.ShowCloseUpAreaWhile(() => ToneTestWindow.InDesign(Doc.State) || LineTestWindow.InDesign(Doc.State));
        host.JobFinished += Finished;
    }

    public void Stop()
    {
        foreach (var w in _open.ToList()) w.Close();
        _open.Clear();
    }

    Document Doc => _host.Document;
    bool HasEtch() => Doc.State.Layers.Any(l => EtchTest.PassesOf(l) is not null);
    List<PathShape> SelectedPaths() => Doc.State.Shapes.Where(s => Doc.Selection.Contains(s.Id)).OfType<PathShape>().ToList();

    /// <summary>The Tests group goes just after Materials.</summary>
    void Add(RibbonButton b) => _host.AddRibbonButton(b with { GroupAfter = "Materials" });

    /// <summary>A burned test: straight to its results (one at a time, as before).</summary>
    void Finished(string? from)
    {
        var s = Doc.State;
        Func<Task>? next =
            HasEtch() ? () => Etch(true)
            : CutTestWindow.InDesign(s) ? () => Cut(true)
            : ToneTestWindow.InDesign(s) ? () => Tone(true)
            : LineTestWindow.InDesign(s) ? () => Line(true)
            : PhotonCanvas.Doc.Bevel.InDesign(s) ? () => Bevel(true)
            : null;
        if (next is not null) Dispatcher.UIThread.Post(() => _ = _host.Guard(next));
        // A depth round burned: measure it next.
        if (ExactDepthWindow.Waiting(s)) Dispatcher.UIThread.Post(() => _ = _host.Guard(ExactDepth));
    }

    async Task Show(Window w)
    {
        _open.Add(w);
        w.Closed += (_, _) => _open.Remove(w);
        await w.ShowDialog(_host.Owner);
        // Tests save what they found with the material.
        _host.RefreshLibrary();
    }

    Task Etch(bool results)
    {
        var m = _host.MachinePanel;
        return Show(new EtchTestWindow(Doc, _host.Library, _host.Machine, () => _host.Photo, m.UpdatePhotoAsync, results,
            d => m.CameraMapFor(d) is { } c ? new EtchTestWindow.ImageMapChoiceInfo(c.Exact, c.Description) : null));
    }

    Task Cut(bool results) =>
        Show(new CutTestWindow(Doc, _host.Library, _host.Machine, () => _host.Photo, _host.MachinePanel.UpdatePhotoAsync, results));

    Task ExactDepth() => Show(new ExactDepthWindow(Doc, _host.MachinePanel, _host.Library, Doc.Selection));

    Task Tone(bool results)
    {
        var m = _host.MachinePanel;
        return Show(new ToneTestWindow(Doc, _host.Library, _host.Machine, () => _host.Photo, m.UpdatePhotoAsync, results, m.CloseUpBedPhotoAsync, m.CloseUpSees, () => m.Lights, m.SetLights));
    }

    Task Line(bool results)
    {
        var m = _host.MachinePanel;
        return Show(new LineTestWindow(Doc, _host.Machine, () => _host.Photo, m.UpdatePhotoAsync, results, m.CloseUpBedPhotoAsync, m.CloseUpSees, () => m.Lights, m.SetLights));
    }

    Task Inlay() => Show(new InlayWindow(Doc, _host.Library, SelectedPaths()));

    Task Bevel(bool results) => Show(new BevelTestWindow(Doc, _host.Library, _host.AppSettings, _host.Machine, results));

    Task Graphene(bool results) => Show(new GrapheneTestWindow(Doc, _host.Library, _host.Machine, results));
}
