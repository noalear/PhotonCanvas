using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Engraving;

/// <summary>Picture → Turn it into → Engraving, opening <see cref="EngravingWindow"/>.</summary>
public sealed class EngravingAddon : IAddon
{
    public void Start(IAddonHost host)
    {
        host.AddRibbonButton(new RibbonButton("Picture", "Turn it into", "Engraving", "IcLine", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => new EngravingWindow(host.Document, im).ShowDialog(host.Owner));
        })
        {
            Tip = "The picture as engraving lines, banknote style: lines that swell in the shadows (straight, following the forms, rings or a spiral)",
            Large = true,
            Enabled = () => host.SelectedPicture is not null,
        });
    }

    public void Stop() { }
}
