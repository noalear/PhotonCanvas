using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Slicer;

/// <summary>
/// Import → 3D & maps → 3D to slices, opening <see cref="StackSliceWindow"/> (stacked layers, egg crate,
/// radial, the assembly booklet and dowel pins). The slicing itself is in Doc (StackSlicer,
/// AssemblyBooklet, DowelPins), which other tools share.
/// </summary>
public sealed class SlicerAddon : IAddon
{
    public void Start(IAddonHost host)
    {
        host.AddRibbonButton(new RibbonButton("Import", "3D & maps", "3D to slices", "IcDepth",
            () => _ = host.Guard(() => new StackSliceWindow(host.Document, host.Machine, host.AppSettings, host.Library).ShowDialog(host.Owner)))
        {
            Tip = "Slice a 3D model (STL, OBJ, STEP) into layers of your material's thickness: cut them, numbered, and glue them up into the model",
            Order = 10,
        });
        host.AddCommand(new AddonCommand("file.slices", "File", "3D to slices…", "",
            () => _ = host.Guard(() => new StackSliceWindow(host.Document, host.Machine, host.AppSettings, host.Library).ShowDialog(host.Owner))));
    }

    public void Stop() { }
}
