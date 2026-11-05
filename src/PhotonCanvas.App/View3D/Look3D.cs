using System.Text.Json;
using PhotonCanvas.Core;

namespace PhotonCanvas.App.View3D;

/// <summary>
/// How every 3D view of a model is drawn: plainly (each piece one colour, outlined: quickest) or realistically (wood
/// grain, charred cut edges, burnt engraving, sunlight with soft shadows, darker corners). One switch for all of them,
/// remembered from one run to the next (in the data folder's view3d.json).
/// </summary>
public static class Look3D
{
    static bool? _realistic;

    static string FilePath => Path.Combine(AppIdentity.DataDir, "view3d.json");

    sealed record Saved(bool Realistic);

    /// <summary>Draw the 3D views realistically.</summary>
    public static bool Realistic
    {
        get => _realistic ??= Load();
        set
        {
            if (value == Realistic) return;
            _realistic = value;
            try
            {
                Directory.CreateDirectory(AppIdentity.DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(value)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// <see cref="Realistic"/> changed. Views listen only while they're on screen (subscribe when attached, unsubscribe when
    /// detached), so a view from an add-on never keeps the add-on in memory.
    /// </summary>
    public static event Action? Changed;

    static bool Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath))?.Realistic ?? false;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        return false;
    }
}
