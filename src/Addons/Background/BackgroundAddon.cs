using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Background;

/// <summary>
/// Picture → Edit → Remove background, the right-click item and the Selection panel's button:
/// makes a picture's backdrop see-through (<see cref="BackgroundWindow"/>).
/// </summary>
public sealed class BackgroundAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Edit", "Remove background", "IcRemoveBg", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => Remove(im));
        })
        {
            Tip = "Make the backdrop see-through so it isn't engraved",
            Large = true,
            Enabled = () => host.SelectedPicture is not null,
            Order = 10,
        });
        host.AddContextMenuItem(new ContextMenuEntry("Remove picture background…", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => Remove(im));
        })
        {
            Visible = () => host.SelectedPicture is not null,
        });
        host.AddCommand(new AddonCommand("picture.background", "Shapes", "Remove picture background…", "", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => Remove(im));
        }));
        host.AddToSlot("properties.picture", ctx =>
        {
            if (ctx.Data is not ImageShape im) return null;
            var b = Button("Remove background…", tip: "Make the picture's backdrop see-through so it isn't engraved.");
            b.Click += (_, _) => _ = host.Guard(() => Remove(im));
            return b;
        });
    }

    public void Stop() { }

    async Task Remove(ImageShape im)
    {
        var doc = _host.Document;
        var w = new BackgroundWindow(im.Source.Pixels, result =>
        {
            var replaced = new ImageShape
            {
                Id = im.Id, Name = im.Name, LayerId = im.LayerId, GroupId = im.GroupId, Transform = im.Transform,
                SizeMm = im.SizeMm, Tone = im.Tone, Clip = im.Clip, Source = ImageSource.FromEncoded(result.ToPng()),
                AddonData = im.AddonData,
            };
            doc.Commit("Remove background", Ops.Replace(doc.State, replaced), [im.Id]);
        });
        await w.ShowDialog(_host.Owner);
    }
}
