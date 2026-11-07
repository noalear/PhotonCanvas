using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.DepthCharge;

/// <summary>
/// Picture → Effects → Depthmap, the Selection panel's Depthmap… button and the right-click
/// item: a depthmap of a picture (or a picture file) via DepthCharge, laid over the original on its
/// own greyscale slices layer.
/// </summary>
public sealed class DepthChargeAddon : IAddon
{
    static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];
    IAddonHost _host = null!;
    DepthChargeService? _service;

    DepthChargeService Service => _service ??= new DepthChargeService();

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Effects", "Depthmap", "IcDepth", () => _ = host.Guard(() => Make(host.SelectedPicture)))
        {
            Tip = "Make a relief depthmap with DepthCharge (select a picture first, or pick a file)",
            Large = true,
            Order = 11,
        });
        host.AddContextMenuItem(new ContextMenuEntry("Depthmap with DepthCharge…", () => _ = host.Guard(() => Make(host.SelectedPicture)))
        {
            Visible = () => host.SelectedPicture is not null,
        });
        host.AddToSlot("properties.picture", ctx =>
        {
            if (ctx.Data is not ImageShape im) return null;
            var b = Button("Depthmap…", tip: "Turn the picture into a relief depthmap with DepthCharge.");
            b.Click += (_, _) => _ = host.Guard(() => Make(im));
            return b;
        });
    }

    /// <summary>Stops DepthCharge only if this add-on started it.</summary>
    public void Stop()
    {
        _service?.Dispose();
        _service = null;
    }

    async Task Make(ImageShape? picture)
    {
        byte[] bytes;
        string name;
        if (picture is not null)
        {
            bytes = picture.Source.Encoded;
            name = string.IsNullOrWhiteSpace(picture.Name) ? "Picture" : picture.Name;
        }
        else
        {
            var files = await _host.Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Picture to turn into a depthmap",
                FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = [.. PictureExtensions.Select(e => "*" + e)] }],
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            bytes = await File.ReadAllBytesAsync(path);
            name = Path.GetFileNameWithoutExtension(path);
        }
        var w = new DepthChargeWindow(Service, _host.AppSettings, bytes, name, (png, label) => Add(png, label, picture));
        await w.ShowDialog(_host.Owner);
    }

    void Add(byte[] png, string label, ImageShape? over)
    {
        var doc = _host.Document;
        var src = ImageSource.FromEncoded(png);
        var state = doc.State;
        var layer = Layer.Default(state.Layers.Count, LayerMode.Image) with
        {
            Name = $"Relief · {label}",
            // Darker = deeper, carved in slices; each slice's lines turned to keep the floor smooth.
            ImageMode = ImageEngraveMode.Slices,
            Dpi = 254,
            LinesPerCm = 200,
            AngleStepDeg = 37,
        };
        state = Ops.AddLayer(state, layer, 0);
        ImageShape shape;
        if (over is not null)
        {
            // Same place and size as the original; a cropped depthmap fits inside it, centred.
            double s = Math.Min(over.SizeMm.X / src.Width, over.SizeMm.Y / src.Height);
            var size = new Vec2(src.Width * s, src.Height * s);
            var inset = (over.SizeMm - size) / 2;
            shape = new ImageShape { Name = label, LayerId = layer.Id, Source = src, SizeMm = size, Transform = Affine.Translate(inset).Then(over.Transform) };
        }
        else
        {
            var size = ImageShape.DefaultSize(src, 100);
            var at = new Rect2(0, 0, _host.Machine.FieldW, _host.Machine.FieldH).Center - size / 2;
            shape = new ImageShape { Name = label, LayerId = layer.Id, Source = src, SizeMm = size, Transform = Affine.Translate(at) };
        }
        doc.Commit("Add depthmap", Ops.AddShapes(state, [shape]), [shape.Id]);
        _host.ActivateLayer(layer.Id);
    }
}
