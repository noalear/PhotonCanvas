using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Engraving;

/// <summary>Picture → Effects → Engraving, opening <see cref="EngravingWindow"/>.</summary>
public sealed class EngravingAddon : IAddon
{
    public void Start(IAddonHost host)
    {
        host.AddRibbonButton(new RibbonButton("Picture", "Effects", "Engraving", "IcLine", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => new EngravingWindow(host.Document, im).ShowDialog(host.Owner));
        })
        {
            Tip = "The picture as engraving lines, banknote style: lines that swell in the shadows (straight, following the forms, rings or a spiral)",
            Large = true,
            Enabled = () => host.SelectedPicture is not null,
            Order = 10,
        });
    }

    public void Stop() { }
}
