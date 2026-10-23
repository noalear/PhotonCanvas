using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using PhotonCanvas.Remote.App;

namespace PhotonCanvas.Remote.Android;

[Activity(
    Label = "PhotonCanvas Remote",
    Theme = "@style/AppTheme",
    Icon = "@mipmap/ic_launcher",
    RoundIcon = "@mipmap/ic_launcher_round",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<RemoteApp>
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        // If the app has to close, say why on the next start.
        string folder = FilesDir!.AbsolutePath;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(folder, e.ExceptionObject);
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => CrashLog.Write(folder, e.Exception);
        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        RemoteApp.Phone = new AndroidPhone(this);
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
