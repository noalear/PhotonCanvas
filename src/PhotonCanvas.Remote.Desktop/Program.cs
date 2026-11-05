// PhotonCanvas Remote in a window: the phone app's screens on the computer (no QR scanning; type or
// paste the address and code from Remote access).
using System.Diagnostics;
using Avalonia;
using PhotonCanvas.Remote;
using PhotonCanvas.Remote.App;

RemoteApp.Phone = new DesktopPhone();
AppBuilder.Configure<RemoteApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);

sealed class DesktopPhone : IPhone
{
    public string DataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotonCanvas Remote");
    public string Version => typeof(DesktopPhone).Assembly.GetName().Version?.ToString(3) ?? "dev";
    public bool CanScan => false;
    public Task<string?> ScanQrAsync() => Task.FromResult<string?>(null);
    public void Vibrate(bool alarm) { }
    public void OpenWeb(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    public bool CanWatch => false;
    public Task<bool> AllowNotificationsAsync() => Task.FromResult(false);
    public void Watch(WatchMode mode, bool jobOnMachine) { }
    public void TestNotification() { }
}
