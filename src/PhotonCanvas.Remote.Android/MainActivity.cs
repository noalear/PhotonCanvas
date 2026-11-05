using Android.App;
using Android.Content.PM;
using Android.Runtime;
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
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<RemoteApp>
{
    readonly Dictionary<int, TaskCompletionSource<bool>> _asks = [];
    int _nextAsk = 100;

    /// <summary>On screen now (Android lets the app start its watcher only then).</summary>
    public bool IsOnScreen { get; private set; }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        // If the app has to close, say why on the next start.
        string folder = FilesDir!.AbsolutePath;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(folder, e.ExceptionObject);
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => CrashLog.Write(folder, e.Exception);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        base.OnResume();
        IsOnScreen = true;
    }

    protected override void OnPause()
    {
        IsOnScreen = false;
        base.OnPause();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        RemoteApp.Phone = new AndroidPhone(this);
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    /// <summary>Ask for a permission; true if given.</summary>
    public Task<bool> AskAsync(string permission)
    {
        var done = new TaskCompletionSource<bool>();
        int code = _nextAsk++;
        _asks[code] = done;
        RequestPermissions([permission], code);
        return done.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (_asks.Remove(requestCode, out var done)) done.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }
}
