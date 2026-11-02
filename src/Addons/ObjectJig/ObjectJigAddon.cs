using PhotonCanvas.App;
using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.ObjectJig;

/// <summary>
/// The object jig maker as an add-on: Camera → Place → Object jig and Find the jig, and two commands, all
/// gone again when it's turned off. Only the camera is used (photos of the bed): it never moves the
/// head or fires the laser; the jig and the objects are marked with the program's own Ship It.
/// </summary>
public sealed class ObjectJigAddon : IAddon
{
    IAddonHost _host = null!;
    Avalonia.Controls.Window? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Camera", "Place", "Object jig", "IcFitPiece", Open)
        {
            Large = true,
            Order = 40,
            Tip = "Photograph an odd-shaped object (a pen, a phone case, a knife handle): cut a jig that holds it, with the design lined up on a page of its own",
        });
        host.AddRibbonButton(new RibbonButton("Camera", "Place", "Find the jig", "IcFrame", Find)
        {
            Large = true,
            Order = 41,
            Enabled = () => JigMaker.GuideOn(host.Document.State) is not null,
            Tip = "On a jig's objects page: photograph the bed, find the jig wherever it lies and line the design up with it",
        });
        host.AddCommand(new AddonCommand("objectjig.open", "Shapes", "Object jig…", "", Open));
        host.AddCommand(new AddonCommand("objectjig.find", "Shapes", "Find the jig…", "", Find));
    }

    void Open() => _ = _host.Guard(async () =>
    {
        if (_open is { } w) { w.Activate(); return; }
        var win = new JigWindow(_host);
        _open = win;
        win.Closed += (_, _) => _open = null;
        await win.ShowDialog(_host.Owner);
    });

    void Find() => _ = _host.Guard(async () =>
    {
        if (_open is { } w) { w.Activate(); return; }
        if (JigMaker.GuideOn(_host.Document.State) is not { } jig)
        {
            await Dialogs.Message(_host.Owner, "Find the jig", "Show a jig's objects page first (the page Object jig makes, with the jig drawn in grey).");
            return;
        }
        if (!_host.IsConnected)
        {
            await Dialogs.Message(_host.Owner, "Find the jig", "Connect to the machine first: finding the jig needs a fresh camera photo.");
            return;
        }
        var win = new FindJigWindow(_host, jig);
        _open = win;
        win.Closed += (_, _) => _open = null;
        await win.ShowDialog(_host.Owner);
    });

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }
}
