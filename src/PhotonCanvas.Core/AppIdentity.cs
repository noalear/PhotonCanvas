namespace PhotonCanvas.Core;

/// <summary>The program's name and where it keeps its files (one place, so renaming the product is one change).</summary>
public static class AppIdentity
{
    public const string ProductName = "PhotonCanvas";
    /// <summary>Folder in the user's home for settings, calibration, keys, the material library and logs.</summary>
    public const string DataFolder = "PhotonCanvas";
    /// <summary>The folder used before the rename.</summary>
    public const string LegacyDataFolder = "YTool";
    /// <summary>Set to use another data folder (tests and the screenshot harness, so they never touch real data).</summary>
    public const string DataDirVariable = "PHOTONCANVAS_DATA";

    /// <summary>
    /// Where everything is kept: <c>%USERPROFILE%\PhotonCanvas</c>. Not under AppData: Windows quietly
    /// redirects AppData writes from programs started by a packaged (Store/MSIX) app into that app's
    /// own storage, so the same program saved to two places depending on how it was opened, and
    /// each run saw only its own saves. A folder in the home directory is the same for everyone.
    /// </summary>
    public static string DataDir =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom ? custom : Path.Combine(Home, DataFolder);

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Every folder earlier versions may have saved into: AppData's PhotonCanvas (and YTool), and the
    /// copies Windows redirected into packaged apps' storage (…\Packages\*\LocalCache\Roaming\…).
    /// Oldest name first, so newer data wins where both have it.
    /// </summary>
    public static IEnumerable<string> EarlierDataDirs()
    {
        foreach (var name in new[] { LegacyDataFolder, DataFolder })
        {
            yield return Path.Combine(AppData, name);
            var packages = Path.Combine(LocalAppData, "Packages");
            IEnumerable<string> redirected = [];
            try { redirected = Directory.EnumerateDirectories(packages).Select(p => Path.Combine(p, "LocalCache", "Roaming", name)).Where(Directory.Exists).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var r in redirected) yield return r;
        }
    }
}
