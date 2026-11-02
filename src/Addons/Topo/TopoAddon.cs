using System.Net.Http.Headers;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Home → Bring in → Topo map: stacked contour maps of any place (or an elevation file), sliced by
/// 3D to slices (this add-on requires it). Heights come from the free Terrain Tiles on AWS, places
/// from OpenStreetMap's Nominatim; both only when the person asks (Find, or moving the area).
/// </summary>
public sealed class TopoAddon : IAddon
{
    IAddonHost _host = null!;
    HttpClient? _http;
    TopoWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Bring in", "Topo map", Icon, Open)
        {
            Tip = "Any place on Earth as a stacked contour map: find it, choose the area and how many layers, and cut the layers (numbered, with where the next one sits marked) to glue up",
            Order = 10,
        });
        host.AddCommand(new AddonCommand("topo.open", "File", "Topo map…", "", Open));
        // Old tiles go when the folder grows big (in the background: it's only tidying).
        var tiles = Path.Combine(host.DataFolder, "tiles");
        _ = Task.Run(() => TerrainTiles.Trim(tiles));
    }

    /// <summary>Mountains in layers (24 × 24 icon path).</summary>
    const string Icon = "M2 18h20v3H2z M4.5 14h14v3h-14z M7 10h9v3H7z M9.5 6h4v3h-4z";

    void Open()
    {
        if (_open is { IsVisible: true } w) { w.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            _http ??= MakeHttp();
            var win = _open = new TopoWindow(_host.Document, _host.Machine, _host.AppSettings, _host.Library, _http, Path.Combine(_host.DataFolder, "tiles"), _host.InvalidateCanvas);
            win.Closed += (_, _) => { if (ReferenceEquals(_open, win)) _open = null; };
            await win.ShowDialog(_host.Owner);
        });
    }

    static HttpClient MakeHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Nominatim's rules: say which program is asking.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PhotonCanvas", Updates.Current));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(topo-map-addon)"));
        return http;
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
        _http?.Dispose();
        _http = null;
    }
}
