using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addons.ShadowBoard;

/// <summary>Saved laser settings the window offers (from the material library).</summary>
/// <param name="EtchMmPerPass">How deep one pass goes, when an etch-rate test measured it.</param>
internal sealed record ShadowRecipe(string Label, Layer Settings, double? EtchMmPerPass = null);

/// <summary>
/// What the shadow board window needs from the program. <see cref="ShadowBoardAddon"/> fills it from the
/// add-on host, so the window and the geometry don't depend on the main window.
/// </summary>
internal interface IShadowBoardHost
{
    /// <summary>The open document (shapes, pages, undo).</summary>
    Document Document { get; }
    /// <summary>The selected shapes with outlines (tools drawn or traced already).</summary>
    IReadOnlyList<PathShape> SelectedPaths { get; }
    /// <summary>The bed area the camera can see (mm).</summary>
    Rect2 WorkArea { get; }
    /// <summary>The machine's last measured distance to the material (mm), if it has one.</summary>
    double? MeasuredDistanceMm { get; }
    /// <summary>Whether a bed photo can be taken at this distance: (ready, what to tell the user).</summary>
    (bool Ready, string Status) CameraStatus(double distanceMm);
    /// <summary>A top-down photo of <paramref name="area"/> straightened onto the bed, <paramref name="mmPerPx"/> a pixel. Only the camera: the head doesn't move and the laser stays off.</summary>
    Task<RasterImage> PhotographBedAsync(Rect2 area, double mmPerPx, double distanceMm);
    /// <summary>Cut recipes from the material library.</summary>
    IReadOnlyList<ShadowRecipe> CutRecipes { get; }
    /// <summary>Engrave (fill) recipes from the material library.</summary>
    IReadOnlyList<ShadowRecipe> EngraveRecipes { get; }
}
