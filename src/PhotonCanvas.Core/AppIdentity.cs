namespace PhotonCanvas.Core;

/// <summary>The program's name and where it keeps its files (one place, so renaming the product is one change).</summary>
public static class AppIdentity
{
    public const string ProductName = "PhotonCanvas";
    /// <summary>Folder under %APPDATA% for settings, calibration, keys and logs.</summary>
    public const string DataFolder = "PhotonCanvas";
    /// <summary>The folder used before the rename (moved over on first start).</summary>
    public const string LegacyDataFolder = "YTool";

    public static string DataDir => Path.Combine(AppData, DataFolder);

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// First start after the rename: copy settings, camera calibration, keys and the pairing
    /// token from the old folder. The old folder is left as it was. Never throws.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            var old = Path.Combine(AppData, LegacyDataFolder);
            if (Directory.Exists(DataDir) || !Directory.Exists(old)) return;
            foreach (var file in Directory.EnumerateFiles(old, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(DataDir, Path.GetRelativePath(old, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the program starts with defaults.
        }
    }
}
