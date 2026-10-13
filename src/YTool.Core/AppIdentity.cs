namespace YTool.Core;

/// <summary>The program's name and where it keeps its files (one place, so renaming the product is one change).</summary>
public static class AppIdentity
{
    public const string ProductName = "YTool Studio";
    /// <summary>Folder under %APPDATA% for settings, calibration, keys and logs.</summary>
    public const string DataFolder = "YTool";

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolder);
}
