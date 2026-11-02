using Avalonia;

namespace PhotonCanvas.App;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Data saved in earlier places (AppData, or where Windows redirected it) comes over first.
        if (PhotonCanvas.Doc.DataMerge.Run() is > 0 and var taken)
            AppLog.Write("data", $"Brought {taken} file(s) over from earlier data folders into {PhotonCanvas.Core.AppIdentity.DataDir}.");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        // A new version downloaded while it ran goes in now that nothing is in use (and it starts again if asked).
        Updater.AtExit();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
