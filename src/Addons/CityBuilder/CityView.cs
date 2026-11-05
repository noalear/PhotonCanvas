using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// The whole map in 3D, light enough to turn round however big it is (<see cref="CityScene"/>): plain
/// blocks and flat colours, no textures or engraving depth. The roofs can be taken off to look in.
/// </summary>
public sealed class CityView() : Model3DView(30, 42, 5, 89)
{
    CityMap? _map;
    CityShow _show = new();

    public CityShow Shown => _show;

    public void Show(CityMap? map, CityShow show, string? message = null)
    {
        _map = map;
        _show = show;
        Message = map is null ? message : null;
        Scene.Clear();
        if (map is not null) CityScene.Build(Scene, map, show);
        InvalidateVisual();
    }

    protected override ulong HiddenGroups => _show.Roofs ? 0 : 1UL << CityScene.RoofGroup;
}
